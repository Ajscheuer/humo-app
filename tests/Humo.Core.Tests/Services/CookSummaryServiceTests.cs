using Humo.Core.Analytics;
using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.Services;

public class CookSummaryServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ICookSummaryService Service => _db.SummaryServiceWith(_settings);

    private Task<Cook> ACookAsync(MeatType meatType = MeatType.Brisket, double weightKg = 6)
        => _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = meatType,
            WeightKg = weightKg,
        });

    [Fact]
    public async Task History_is_empty_before_any_cook_finishes()
    {
        await ACookAsync();

        // A running cook is not history. Listing it would put a cook with no
        // duration at the top of a list of durations.
        Assert.Empty(await Service.GetHistoryAsync());
    }

    [Fact]
    public async Task History_lists_finished_cooks_newest_first()
    {
        var first = await ACookAsync(MeatType.PorkRibs);
        await _db.Service.FinishCookAsync(first.Id);

        _db.Clock.Advance(TimeSpan.FromDays(7));
        var second = await ACookAsync(MeatType.Brisket);
        await _db.Service.FinishCookAsync(second.Id);

        // Newest first: the cook you just did is the one you want to look at.
        Assert.Equal(
            [second.Id, first.Id],
            (await Service.GetHistoryAsync()).Select(c => c.Id));
    }

    [Fact]
    public async Task A_summary_for_a_cook_that_does_not_exist_is_null()
    {
        Assert.Null(await Service.GetSummaryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_summary_carries_the_cook_its_rig_and_its_statistics()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(12));
        await _db.Service.FinishCookAsync(cook.Id);

        var summary = await Service.GetSummaryAsync(cook.Id);

        Assert.NotNull(summary);
        Assert.Equal(cook.Id, summary.Cook.Id);
        Assert.Equal(CookService.DefaultEquipmentName, summary.Equipment!.Name);
        Assert.Equal(TimeSpan.FromHours(12), summary.Statistics.Duration);
        Assert.Equal(TimeSpan.FromHours(2), summary.Statistics.TimePerKg);
    }

    [Fact]
    public async Task A_summary_survives_its_rig_being_deleted()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);
        await _db.EquipmentService.DeleteAsync(cook.EquipmentId);

        var summary = await Service.GetSummaryAsync(cook.Id);

        // The rig is soft-deleted, so the cook's own history stays readable. The
        // page says "rig deleted" rather than failing to open.
        Assert.NotNull(summary);
        Assert.Null(summary.Equipment);
        Assert.Equal(EquipmentType.Offset, summary.Cook.PitType);
    }

    [Fact]
    public async Task The_chart_shows_readings_in_the_users_unit()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var cook = await ACookAsync();

        await LogAsync(cook, meatTempC: 74, pitTempC: 121);
        await _db.Service.FinishCookAsync(cook.Id);

        var chart = (await Service.GetSummaryAsync(cook.Id))!.Chart;

        var meat = chart.Series.Single(s => s.Kind == ChartSeriesKind.Meat);
        var pit = chart.Series.Single(s => s.Kind == ChartSeriesKind.Pit);

        Assert.Equal(165.2, meat.Points.Single().Value, precision: 1);
        Assert.Equal(249.8, pit.Points.Single().Value, precision: 1);

        // The axis label travels with the numbers, so it cannot drift from them.
        Assert.Equal(AppStrings.Unit_Fahrenheit_Short, chart.TemperatureUnitKey);
    }

    [Fact]
    public async Task The_same_chart_reads_in_Celsius_for_a_Celsius_cook()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var cook = await ACookAsync();
        await LogAsync(cook, meatTempC: 74, pitTempC: 121);
        await _db.Service.FinishCookAsync(cook.Id);

        var chart = (await Service.GetSummaryAsync(cook.Id))!.Chart;

        Assert.Equal(
            74,
            chart.Series.Single(s => s.Kind == ChartSeriesKind.Meat).Points.Single().Value,
            precision: 5);
        Assert.Equal(AppStrings.Unit_Celsius_Short, chart.TemperatureUnitKey);
    }

    [Fact]
    public async Task A_cook_with_no_readings_has_an_empty_chart_rather_than_a_broken_one()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var summary = await Service.GetSummaryAsync(cook.Id);

        Assert.True(summary!.Chart.IsEmpty);
        Assert.Empty(summary.Chart.Series);
        Assert.Null(summary.Statistics.PeakMeatTempC);
        Assert.Equal(0, summary.Statistics.ReadingCount);
    }

    [Fact]
    public async Task Chart_points_are_ordered_by_when_they_were_taken()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var cook = await ACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(4));
        await LogAsync(cook, meatTempC: 68);

        // A reading remembered two hours late, entered after a newer one.
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 55,
            RecordedAt = _db.Clock.UtcNow - TimeSpan.FromHours(2),
        });
        await _db.Service.FinishCookAsync(cook.Id);

        var meat = (await Service.GetSummaryAsync(cook.Id))!
            .Chart.Series.Single(s => s.Kind == ChartSeriesKind.Meat);

        // A line drawn in typing order would zigzag backwards through time.
        Assert.Equal([55.0, 68.0], meat.Points.Select(p => p.Value));
    }

    [Fact]
    public async Task Peak_temperatures_come_from_the_readings()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var cook = await ACookAsync();

        foreach (var (meat, pit) in new[] { (40.0, 110.0), (68.0, 135.0), (60.0, 120.0) })
        {
            _db.Clock.Advance(TimeSpan.FromHours(1));
            await LogAsync(cook, meatTempC: meat, pitTempC: pit);
        }

        await _db.Service.FinishCookAsync(cook.Id);
        var statistics = (await Service.GetSummaryAsync(cook.Id))!.Statistics;

        // Highest, not last: meat temp dips when it is wrapped or the probe moves.
        Assert.Equal(68, statistics.PeakMeatTempC);
        Assert.Equal(135, statistics.PeakPitTempC);
        Assert.Equal(3, statistics.ReadingCount);
    }

    [Fact]
    public async Task Fuel_and_milestones_are_marked_on_the_time_axis()
    {
        var cook = await ACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(2));
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = cook.EquipmentId,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Medium,
        });

        _db.Clock.Advance(TimeSpan.FromHours(1));
        await _db.Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });

        await _db.Service.FinishCookAsync(cook.Id);
        var chart = (await Service.GetSummaryAsync(cook.Id))!.Chart;

        // These are what turn a temperature graph into a story: a pit dip half an
        // hour after a large split reads differently from the same dip alone.
        Assert.Equal(
            [ChartMarkerKind.Fuel, ChartMarkerKind.Milestone],
            chart.Markers.Select(m => m.Kind));
        Assert.Equal(AppStrings.Fuel_Add, chart.Markers[0].LabelKey);
        Assert.Equal(EnumDisplay.KeyFor(EventType.Wrapped), chart.Markers[1].LabelKey);
        Assert.Equal(1, chart.Markers.Count(m => m.Kind == ChartMarkerKind.Fuel));
    }

    [Fact]
    public async Task Markers_are_ordered_by_time_regardless_of_kind()
    {
        var cook = await ACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(1));
        await _db.Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Spritzed,
        });

        _db.Clock.Advance(TimeSpan.FromHours(1));
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = cook.EquipmentId,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Small,
        });

        await _db.Service.FinishCookAsync(cook.Id);
        var markers = (await Service.GetSummaryAsync(cook.Id))!.Chart.Markers;

        Assert.Equal(
            [ChartMarkerKind.Milestone, ChartMarkerKind.Fuel],
            markers.Select(m => m.Kind));
    }

    [Fact]
    public async Task Fuel_from_before_this_cook_is_not_charted_against_it()
    {
        var rig = await _db.Service.GetOrCreateDefaultEquipmentAsync();

        // Getting the fire up to temperature yesterday, for a different cook.
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = rig.Id,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Large,
        });

        _db.Clock.Advance(TimeSpan.FromDays(1));
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        // Fuel belongs to the rig, so the summary has to window it to this cook
        // or every chart would carry the rig's entire history.
        Assert.Empty((await Service.GetSummaryAsync(cook.Id))!.Chart.Markers);
        Assert.Equal(0, (await Service.GetSummaryAsync(cook.Id))!.Statistics.FuelEventCount);
    }

    [Fact]
    public async Task Two_cooks_on_one_fire_share_the_pit_line()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var rig = await _db.Service.GetOrCreateDefaultEquipmentAsync();

        var brisket = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });
        var ribs = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkRibs,
            WeightKg = 1.5,
            EquipmentId = rig.Id,
        });

        _db.Clock.Advance(TimeSpan.FromHours(1));
        await LogAsync(brisket, meatTempC: 50, pitTempC: 130);

        await _db.Service.FinishCookAsync(brisket.Id);
        await _db.Service.FinishCookAsync(ribs.Id);

        var ribsChart = (await Service.GetSummaryAsync(ribs.Id))!.Chart;

        // There was one fire. The ribs' own meat line is empty, but the pit line
        // is the rig's and belongs on both charts.
        Assert.DoesNotContain(ribsChart.Series, s => s.Kind == ChartSeriesKind.Meat);
        Assert.Equal(
            130,
            ribsChart.Series.Single(s => s.Kind == ChartSeriesKind.Pit).Points.Single().Value);
    }

    [Fact]
    public async Task A_reading_back_dated_before_the_cook_started_still_appears()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var cook = await ACookAsync();

        // Logged as taken just before the cook record was created -- the meat
        // went on while the app was still being opened.
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 4,
            RecordedAt = cook.StartedAt - TimeSpan.FromMinutes(10),
        });
        await _db.Service.FinishCookAsync(cook.Id);

        var meat = (await Service.GetSummaryAsync(cook.Id))!
            .Chart.Series.Single(s => s.Kind == ChartSeriesKind.Meat);

        // The window widens to hold it rather than silently dropping it.
        Assert.Equal(4, meat.Points.Single().Value);
    }

    private Task LogAsync(Cook cook, double meatTempC, double? pitTempC = null)
        => _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = meatTempC,
            PitTempC = pitTempC,
        });
}
