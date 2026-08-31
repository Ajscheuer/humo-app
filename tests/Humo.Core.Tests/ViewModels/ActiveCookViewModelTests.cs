using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class ActiveCookViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ActiveCookViewModel CreateViewModel()
        => new(_db.Service, _settings, _localizer, _db.Clock, _navigation);

    private Task<Cook> StartACookAsync()
        => _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6.0,
        });

    /// <summary>A loaded screen with a cook running on it — the usual starting point.</summary>
    private async Task<ActiveCookViewModel> LoadedWithACookAsync()
    {
        await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private static Task LogAsync(ActiveCookViewModel vm, double meatTemp)
    {
        vm.MeatTempInput = meatTemp;
        return vm.LogTemperatureCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Nothing_is_shown_when_no_cook_is_running()
    {
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        // First launch, and every launch after a finished cook. The screen must
        // cope with an empty database rather than assuming a cook exists.
        Assert.False(vm.HasActiveCook);
        Assert.False(vm.HasReadings);
        Assert.Empty(vm.Entries);
        Assert.Null(vm.LastMeatTemp);
        Assert.Equal(TimeSpan.Zero, vm.Elapsed);
    }

    [Fact]
    public async Task Loading_picks_up_the_cook_that_is_already_running()
    {
        var started = await StartACookAsync();
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        // Reopening the app mid-cook is the normal case, not the exception.
        Assert.True(vm.HasActiveCook);
        Assert.Equal(started.Id, vm.Cook!.Id);
    }

    [Fact]
    public async Task Elapsed_time_is_measured_from_the_clock_not_from_the_readings()
    {
        var vm = await LoadedWithACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(4.5));

        // A cook with no readings for four hours has still been running four
        // hours; elapsed time must not be derived from the last entry.
        Assert.Equal(TimeSpan.FromHours(4.5), vm.Elapsed);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(95, "01:35")]
    [InlineData(60 * 26 + 10, "26:10")]
    public async Task Elapsed_time_reads_as_hours_and_minutes(int minutes, string expected)
    {
        var vm = await LoadedWithACookAsync();

        _db.Clock.Advance(TimeSpan.FromMinutes(minutes));

        // A 26-hour brisket must read 26:10, not 02:10 with the day dropped.
        Assert.Equal(expected, vm.ElapsedDisplay);
    }

    [Fact]
    public void Elapsed_time_reads_as_zero_when_no_cook_is_running()
    {
        Assert.Equal("00:00", CreateViewModel().ElapsedDisplay);
    }

    [Fact]
    public async Task Elapsed_time_is_shown_with_the_same_digits_in_Spanish()
    {
        var vm = await LoadedWithACookAsync();
        _db.Clock.Advance(TimeSpan.FromMinutes(95));

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        // The labels around it translate; the clock face does not.
        Assert.Equal("01:35", vm.ElapsedDisplay);
    }

    [Fact]
    public async Task Elapsed_time_stops_when_the_cook_finishes()
    {
        var vm = await LoadedWithACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(8));
        await vm.FinishCommand.ExecuteAsync(null);
        _db.Clock.Advance(TimeSpan.FromDays(2));

        // Otherwise a finished cook left on screen keeps counting, and the
        // duration the user reads is whenever they next looked at the phone.
        Assert.Equal(TimeSpan.FromHours(8), vm.Elapsed);
    }

    [Fact]
    public async Task A_finished_cook_can_no_longer_be_logged_against()
    {
        var vm = await LoadedWithACookAsync();

        await vm.FinishCommand.ExecuteAsync(null);
        vm.MeatTempInput = 70;

        Assert.False(vm.HasActiveCook);
        Assert.True(vm.IsFinished);
        Assert.False(vm.CanLogTemperature);
        Assert.False(vm.LogTemperatureCommand.CanExecute(null));
        Assert.False(vm.FinishCommand.CanExecute(null));
    }

    [Fact]
    public async Task Finishing_leaves_the_cook_on_screen()
    {
        var started = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.FinishCommand.ExecuteAsync(null);

        // The cook is gone from "active", but the screen still has something to
        // show; blanking it the instant the user taps Finish loses the result.
        Assert.Equal(started.Id, vm.Cook!.Id);
        Assert.NotNull(vm.Cook.FinishedAt);
        Assert.Equal(CookFinishReason.Manual, vm.Cook.FinishReason);
    }

    [Fact]
    public async Task Logging_a_reading_in_Fahrenheit_stores_Celsius()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MeatTempInput = 165;
        vm.PitTempInput = 250;
        await vm.LogTemperatureCommand.ExecuteAsync(null);

        var stored = Assert.Single(await _db.TempEntries.GetForCookAsync(cook.Id));
        Assert.Equal(73.888888, stored.MeatTempC, precision: 5);

        // The pit reading belongs to the rig, and is converted on the same path.
        var pit = Assert.Single(await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(121.111111, pit.PitTempC, precision: 5);
    }

    [Fact]
    public async Task A_reading_with_no_pit_temperature_writes_nothing_against_the_rig()
    {
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await LogAsync(vm, 60);

        // Checking the meat without opening the lid is normal. An absent pit
        // reading must stay absent rather than being stored as zero.
        Assert.Empty(await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Readings_are_shown_back_in_the_users_unit()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var vm = await LoadedWithACookAsync();

        await LogAsync(vm, 165);

        // Round-trip: what the user typed is what the user reads back, even
        // though nothing between here and the database saw Fahrenheit.
        Assert.Equal(165, Assert.Single(vm.Entries).MeatTemp, precision: 5);
        Assert.Equal(165, vm.LastMeatTemp!.Value, precision: 5);
        Assert.True(vm.HasReadings);
        Assert.Equal("°F", vm.TemperatureUnitSymbol);
    }

    [Fact]
    public async Task The_same_readings_are_shown_in_Celsius_for_a_Celsius_cook()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var vm = await LoadedWithACookAsync();

        await LogAsync(vm, 74);

        Assert.Equal(74, Assert.Single(vm.Entries).MeatTemp, precision: 5);
        Assert.Equal("°C", vm.TemperatureUnitSymbol);
    }

    [Fact]
    public async Task The_sheet_is_cleared_after_a_reading_is_logged()
    {
        var vm = await LoadedWithACookAsync();
        vm.MeatTempInput = 60;
        vm.PitTempInput = 120;
        vm.NoteInput = "Wrapped";
        vm.UseCustomTime = true;

        await vm.LogTemperatureCommand.ExecuteAsync(null);

        // A number left in the box is a duplicate reading waiting for a tired
        // cook to tap Log again at 3am.
        Assert.Null(vm.MeatTempInput);
        Assert.Null(vm.PitTempInput);
        Assert.Null(vm.NoteInput);
        Assert.False(vm.UseCustomTime);
        Assert.False(vm.CanLogTemperature);
    }

    [Fact]
    public async Task A_back_dated_reading_does_not_become_the_headline_number()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var vm = await LoadedWithACookAsync();

        _db.Clock.Advance(TimeSpan.FromHours(3));
        await LogAsync(vm, 68);

        // Remembering a reading from an hour ago and typing it in now.
        var anHourAgo = TimeZoneInfo.ConvertTime(
            _db.Clock.UtcNow - TimeSpan.FromHours(1), _db.Clock.LocalTimeZone);

        vm.MeatTempInput = 60;
        vm.UseCustomTime = true;
        vm.RecordedDate = anHourAgo.Date;
        vm.RecordedTime = anHourAgo.TimeOfDay;
        await vm.LogTemperatureCommand.ExecuteAsync(null);

        // Latest by when it was taken, not by when it was typed. Showing 60 °C
        // here would read as the meat having gone backwards.
        Assert.Equal(68, vm.LastMeatTemp!.Value, precision: 5);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public async Task A_custom_time_is_stored_as_the_instant_it_names_in_the_users_zone()
    {
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        // 11pm the previous evening, as the user's phone shows it. The test clock
        // sits at UTC-06:00, so that is 05:00 UTC the following morning.
        vm.MeatTempInput = 55;
        vm.UseCustomTime = true;
        vm.RecordedDate = new DateTime(2026, 3, 13);
        vm.RecordedTime = new TimeSpan(23, 0, 0);
        await vm.LogTemperatureCommand.ExecuteAsync(null);

        // Storing the wall-clock reading as if it were UTC would place this
        // overnight reading six hours into the future.
        var stored = Assert.Single(await _db.TempEntries.GetForCookAsync(cook.Id));
        Assert.Equal(
            new DateTimeOffset(2026, 3, 14, 5, 0, 0, TimeSpan.Zero),
            stored.RecordedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Leaving_the_time_alone_records_the_reading_as_now()
    {
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        _db.Clock.Advance(TimeSpan.FromHours(2));
        await LogAsync(vm, 60);

        // The default path does no timezone arithmetic at all, which is what
        // makes it the one that cannot be got wrong.
        var stored = Assert.Single(await _db.TempEntries.GetForCookAsync(cook.Id));
        Assert.Equal(_db.Clock.UtcNow, stored.RecordedAt);
    }

    [Fact]
    public async Task A_note_is_kept_with_the_reading()
    {
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MeatTempInput = 71;
        vm.NoteInput = "  Wrapped in butcher paper  ";
        await vm.LogTemperatureCommand.ExecuteAsync(null);

        var stored = Assert.Single(await _db.TempEntries.GetForCookAsync(cook.Id));
        Assert.Equal("Wrapped in butcher paper", stored.Note);
        Assert.Equal("Wrapped in butcher paper", Assert.Single(vm.Entries).Note);
    }

    [Fact]
    public async Task A_blank_note_is_stored_as_no_note()
    {
        var cook = await StartACookAsync();
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MeatTempInput = 71;
        vm.NoteInput = "   ";
        await vm.LogTemperatureCommand.ExecuteAsync(null);

        // Whitespace is not a note, and storing it as one makes every "has a
        // note" check downstream wrong.
        Assert.Null(Assert.Single(await _db.TempEntries.GetForCookAsync(cook.Id)).Note);
    }

    [Fact]
    public async Task Readings_are_listed_oldest_first()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Celsius);
        var vm = await LoadedWithACookAsync();

        foreach (var temp in new[] { 40.0, 55.0, 68.0 })
        {
            _db.Clock.Advance(TimeSpan.FromHours(1));
            await LogAsync(vm, temp);
        }

        // A cook reads the list as the story of the cook so far.
        Assert.Equal([40.0, 55.0, 68.0], vm.Entries.Select(e => e.MeatTemp));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public async Task A_reading_cannot_be_logged_without_a_sensible_meat_temperature(double? meatTemp)
    {
        var vm = await LoadedWithACookAsync();

        vm.MeatTempInput = meatTemp;

        // The meat temperature is the reading. Everything else on the sheet is
        // optional, but there is nothing to record without this.
        Assert.False(vm.CanLogTemperature);
        Assert.False(vm.LogTemperatureCommand.CanExecute(null));
    }

    [Fact]
    public async Task Logging_is_refused_while_no_cook_is_running()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MeatTempInput = 70;

        Assert.False(vm.CanLogTemperature);
        Assert.False(vm.LogTemperatureCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_empty_state_leads_to_the_start_form()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.StartANewCookCommand.ExecuteAsync(null);

        await _navigation.Received(1).GoToAsync(AppRoutes.StartCook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reading_is_listed_at_the_time_the_user_would_read_off_a_clock()
    {
        var vm = await LoadedWithACookAsync();

        // 06:00 UTC, which is midnight in the test clock's UTC-06:00 zone.
        await LogAsync(vm, 40);

        // Listing the stored UTC value would tell a cook they logged this at 6am.
        var entry = Assert.Single(vm.Entries);
        Assert.Equal(new DateTime(2026, 3, 14, 0, 0, 0), entry.RecordedAtLocal.DateTime);
        Assert.Equal(_db.Clock.UtcNow, entry.RecordedAt);
    }

    [Fact]
    public void Finishing_without_a_cook_is_refused()
    {
        var vm = CreateViewModel();

        Assert.False(vm.FinishCommand.CanExecute(null));
    }

    [Fact]
    public async Task Choosing_Spanish_does_not_change_the_temperature_unit()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var vm = await LoadedWithACookAsync();

        await LogAsync(vm, 165);
        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        // The rule from CLAUDE.md, exercised end to end: an American cook with a
        // Spanish phone still reads °F.
        Assert.Equal("°F", vm.TemperatureUnitSymbol);
        Assert.Equal(165, vm.LastMeatTemp!.Value, precision: 5);
    }
}
