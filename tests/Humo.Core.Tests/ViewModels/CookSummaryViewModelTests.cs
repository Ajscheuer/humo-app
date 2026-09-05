using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class CookSummaryViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CookSummaryViewModel CreateViewModel()
        => new(_db.SummaryServiceWith(_settings), _settings, _localizer);

    private Task<Cook> ACookAsync(double weightKg = 6)
        => _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = weightKg,
        });

    [Fact]
    public async Task A_cook_that_no_longer_exists_says_so()
    {
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(Guid.NewGuid());

        // Deleted on another device between the list loading and this tap. The
        // page explains rather than showing a screen of em dashes.
        Assert.False(vm.HasSummary);
        Assert.Equal(_localizer[AppStrings.Summary_NotFound], vm.ErrorMessage);
    }

    [Fact]
    public async Task A_finished_cook_shows_its_duration_and_time_per_kilo()
    {
        var cook = await ACookAsync(weightKg: 6);
        _db.Clock.Advance(TimeSpan.FromHours(12));
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        Assert.True(vm.HasSummary);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("12:00", vm.DurationDisplay);
        Assert.Equal("02:00", vm.TimePerWeightDisplay);
        Assert.Equal(AppStrings.Summary_TimePerKg, vm.TimePerWeightLabelKey);
    }

    [Fact]
    public async Task Time_per_weight_follows_the_users_weight_unit()
    {
        _settings.WeightUnit.Returns(WeightUnit.Pounds);

        // Exactly 10 lb of brisket, cooked for exactly 10 hours.
        var cook = await ACookAsync(weightKg: 10 * 0.45359237);
        _db.Clock.Advance(TimeSpan.FromHours(10));
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        // A cook who thinks in pounds must be given time per pound, not the
        // per-kilogram figure with a pound label on it. Ten hours over ten
        // pounds is one hour a pound; the same cook per kilo reads 02:12.
        Assert.Equal("01:00", vm.TimePerWeightDisplay);
        Assert.Equal(AppStrings.Summary_TimePerLb, vm.TimePerWeightLabelKey);

        // The underlying statistic stays metric, because trends must compare.
        Assert.Equal(
            TimeSpan.FromHours(10) / (10 * 0.45359237),
            vm.Summary!.Statistics.TimePerKg);
    }

    [Fact]
    public async Task Time_per_weight_is_blank_for_a_cook_with_no_usable_weight()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(12));
        await _db.Service.FinishCookAsync(cook.Id);

        // A weight that never should have reached storage, but might via sync.
        var stored = (await _db.Cooks.GetAsync(cook.Id))!;
        stored.WeightKg = 0;
        await _db.Cooks.SaveAsync(stored);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        // Dividing by it would render as infinity beside real figures.
        Assert.Equal(_localizer[AppStrings.Summary_Unknown], vm.TimePerWeightDisplay);
        Assert.Equal("12:00", vm.DurationDisplay);
    }

    [Fact]
    public async Task A_running_cook_shows_placeholders_rather_than_zeroes()
    {
        var cook = await ACookAsync();

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        var placeholder = _localizer[AppStrings.Summary_Unknown];
        Assert.Equal(placeholder, vm.DurationDisplay);
        Assert.Equal(placeholder, vm.TimePerWeightDisplay);
    }

    [Fact]
    public async Task Peak_temperatures_are_shown_in_the_users_unit()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var cook = await ACookAsync();
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 74,
            PitTempC = 121,
        });
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        Assert.Equal("165.2°F", vm.PeakMeatTempDisplay);
        Assert.Equal("249.8°F", vm.PeakPitTempDisplay);
    }

    [Fact]
    public async Task A_cook_with_no_readings_shows_placeholders_for_its_peaks()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        var placeholder = _localizer[AppStrings.Summary_Unknown];
        Assert.Equal(placeholder, vm.PeakMeatTempDisplay);
        Assert.Equal(placeholder, vm.PeakPitTempDisplay);
        Assert.False(vm.HasChart);
    }

    [Fact]
    public async Task An_auto_finished_cook_is_flagged_as_estimated()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(30));
        await _db.Service.FinishCookAsync(cook.Id);

        // What the auto-finish job will write once it exists.
        var stored = (await _db.Cooks.GetAsync(cook.Id))!;
        stored.FinishReason = CookFinishReason.AutoFinished;
        await _db.Cooks.SaveAsync(stored);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        // The duration is still shown, but the user is told it is inferred. Being
        // quietly handed a guess as a measurement is the failure here.
        Assert.True(vm.IsEstimated);
        Assert.Equal("30:00", vm.DurationDisplay);
    }

    [Fact]
    public async Task The_rig_name_is_shown_and_survives_its_deletion()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);
        Assert.Equal(CookService.DefaultEquipmentName, vm.EquipmentName);

        await _db.EquipmentService.DeleteAsync(cook.EquipmentId);
        var afterDelete = CreateViewModel();
        await afterDelete.LoadCommand.ExecuteAsync(cook.Id);

        Assert.Equal(_localizer[AppStrings.Summary_RigDeleted], afterDelete.EquipmentName);
    }

    [Fact]
    public async Task The_chart_arrives_ready_to_draw()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var cook = await ACookAsync();
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 68,
        });
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        Assert.True(vm.HasChart);
        Assert.Equal(68, vm.Chart.Series.Single().Points.Single().Value);
    }

    [Fact]
    public void The_chart_is_empty_rather_than_null_before_anything_loads()
    {
        var vm = CreateViewModel();

        // The page binds to this before the load command runs.
        Assert.NotNull(vm.Chart);
        Assert.True(vm.Chart.IsEmpty);
        Assert.False(vm.HasChart);
    }

    [Fact]
    public async Task Counts_are_reported_for_readings_and_fuel()
    {
        var cook = await ACookAsync();

        for (var i = 0; i < 3; i++)
        {
            _db.Clock.Advance(TimeSpan.FromHours(1));
            await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
            {
                CookId = cook.Id,
                MeatTempC = 40 + i,
            });
        }

        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = cook.EquipmentId,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Medium,
        });

        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        Assert.Equal("3", vm.ReadingCountDisplay);
        Assert.Equal("1", vm.FuelLoadCountDisplay);
    }

    [Fact]
    public async Task Choosing_Spanish_translates_the_labels_but_not_the_unit()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var cook = await ACookAsync();
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 100,
        });
        _db.Clock.Advance(TimeSpan.FromHours(8));
        await _db.Service.FinishCookAsync(cook.Id);

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(cook.Id);

        Assert.Equal("Resumen de la cocción", vm.Title);
        Assert.Equal("°F", vm.TemperatureUnitSymbol);

        // The number follows the culture -- Spanish uses a decimal comma -- while
        // the unit it is measured in follows the setting, not the language.
        Assert.Equal("212°F", vm.PeakMeatTempDisplay);
        Assert.Equal("08:00", vm.DurationDisplay);
    }
}
