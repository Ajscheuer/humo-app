using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Time;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;

namespace Humo.Core.ViewModels;

/// <summary>
/// One logged reading, ready to display.
/// </summary>
/// <param name="RecordedAt">The instant, in UTC, as stored.</param>
/// <param name="RecordedAtLocal">
/// The same instant in the user's zone. Converted here rather than in a XAML
/// format string, which would render the stored UTC value and quietly show every
/// reading in the wrong hour.
/// </param>
/// <param name="MeatTemp">The reading in the user's unit, not in storage units.</param>
public sealed record TempEntryDisplay(
    DateTimeOffset RecordedAt,
    DateTimeOffset RecordedAtLocal,
    double MeatTemp,
    string? Note);

/// <summary>One milestone, ready to display. The type is a resource key, not text.</summary>
public sealed record EventDisplay(
    DateTimeOffset RecordedAt,
    DateTimeOffset RecordedAtLocal,
    string TypeKey,
    string? Note);

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record EventTypeOption(EventType Value, string DisplayNameKey);

/// <summary>
/// The active cook screen — the app's centre of gravity.
/// <para>
/// It must be usable one-handed, outdoors, at 3am, with cold hands, so the
/// surface here is deliberately small: what is happening now, and the two
/// actions that matter.
/// </para>
/// </summary>
public sealed partial class ActiveCookViewModel : ObservableObject
{
    private readonly ICookService _cooks;
    private readonly IUserSettings _settings;
    private readonly ILocalizer _localizer;
    private readonly IClock _clock;
    private readonly INavigationService _navigation;

    public ActiveCookViewModel(
        ICookService cooks,
        IUserSettings settings,
        ILocalizer localizer,
        IClock clock,
        INavigationService navigation)
    {
        _cooks = cooks;
        _settings = settings;
        _localizer = localizer;
        _clock = clock;
        _navigation = navigation;

        SeedRecordedAtWithNow();
    }

    public ObservableCollection<TempEntryDisplay> Entries { get; } = [];

    [ObservableProperty]
    private Cook? _cook;

    /// <summary>Most recent meat reading in the user's unit, or null if none yet.</summary>
    [ObservableProperty]
    private double? _lastMeatTemp;

    /// <summary>
    /// True only while a cook is actually running. A finished cook is still
    /// loaded — the screen keeps showing what happened — but nothing may be
    /// logged against it, so the logging controls bind to this and not to
    /// "a cook is loaded".
    /// </summary>
    public bool HasActiveCook => Cook is { IsFinished: false };

    public bool IsFinished => Cook is { IsFinished: true };

    /// <summary>
    /// A cook is on screen, running or finished. Distinct from
    /// <see cref="HasActiveCook"/>: the numbers stay visible after Finish, the
    /// controls that write to the cook do not.
    /// </summary>
    public bool IsCookLoaded => Cook is not null;

    public bool HasReadings => Entries.Count > 0;

    /// <summary>Meat reading being entered, in the user's unit. Required to log.</summary>
    [ObservableProperty]
    private double? _meatTempInput;

    /// <summary>Pit reading being entered, in the user's unit. Optional.</summary>
    [ObservableProperty]
    private double? _pitTempInput;

    [ObservableProperty]
    private string? _noteInput;

    /// <summary>
    /// Off means "now", which is the overwhelmingly common case and the one that
    /// must take no taps. On reveals the date and time fields below.
    /// </summary>
    [ObservableProperty]
    private bool _useCustomTime;

    /// <summary>Local date of the reading, used only when <see cref="UseCustomTime"/> is on.</summary>
    [ObservableProperty]
    private DateTime _recordedDate;

    /// <summary>Local time of day of the reading, used only when <see cref="UseCustomTime"/> is on.</summary>
    [ObservableProperty]
    private TimeSpan _recordedTime;

    /// <summary>
    /// A reading needs a meat temperature and a cook to belong to; everything
    /// else on the sheet is optional.
    /// </summary>
    public bool CanLogTemperature
        => HasActiveCook && MeatTempInput is { } temp && double.IsFinite(temp);

    public string TemperatureUnitSymbol => _localizer[
        _settings.TemperatureUnit == TemperatureUnit.Fahrenheit
            ? AppStrings.Unit_Fahrenheit_Short
            : AppStrings.Unit_Celsius_Short];

    /// <summary>
    /// How long the cook has been running. Measured from the injected clock, so
    /// it is deterministic in tests rather than whatever the wall clock said.
    /// </summary>
    public TimeSpan Elapsed => Cook is null
        ? TimeSpan.Zero
        : (Cook.FinishedAt ?? _clock.UtcNow) - Cook.StartedAt;

    /// <summary>
    /// Elapsed time as the screen shows it: hours and minutes, zero-padded.
    /// <para>
    /// Formatted here rather than in a XAML converter because the rounding is a
    /// decision, not presentation trivia — a cook 90 minutes in reads "01:30",
    /// never "1.5". Digits are culture-invariant; the labels beside them are not,
    /// and those come from resources.
    /// </para>
    /// </summary>
    public string ElapsedDisplay
    {
        get
        {
            var elapsed = Elapsed;

            // Total hours, not TimeSpan.Hours: a 26-hour brisket reads "26:10",
            // not "02:10" with the day silently dropped.
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}");
        }
    }

    /// <summary>
    /// Opens the fuel sheet for the fire this cook is on.
    /// <para>
    /// Tap one of the two-tap path. It carries the rig, because fuel belongs to
    /// the fire — with two cooks on one smoker the sheet still never has to ask
    /// which cook this is for.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveCook))]
    private Task AddFuelAsync(CancellationToken cancellationToken)
    {
        if (Cook is null)
        {
            throw new InvalidOperationException("There is no cook in progress to log fuel against.");
        }

        return _navigation.GoToAsync(
            AppRoutes.FuelSheetFor(Cook.EquipmentId, Cook.Id), cancellationToken);
    }

    /// <summary>Milestones logged on this cook, oldest first.</summary>
    public ObservableCollection<EventDisplay> Milestones { get; } = [];

    /// <summary>The milestone buttons: wrapped, spritzed, rested, other.</summary>
    public IReadOnlyList<EventTypeOption> EventTypes { get; } =
        EnumDisplay.EventTypesInDisplayOrder
            .Select(t => new EventTypeOption(t, EnumDisplay.KeyFor(t)))
            .ToList();

    public bool HasMilestones => Milestones.Count > 0;

    /// <summary>
    /// Records a milestone. One tap from the active cook screen, with the note
    /// left for anyone who wants it — wrapping a brisket is a thing you do with
    /// one hand while holding foil in the other.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveCook))]
    private async Task LogMilestoneAsync(EventType type, CancellationToken cancellationToken)
    {
        if (Cook is null)
        {
            throw new InvalidOperationException("There is no cook in progress to log against.");
        }

        await _cooks.LogEventAsync(
            new LogEventRequest { CookId = Cook.Id, Type = type },
            cancellationToken);

        await ReloadMilestonesAsync(cancellationToken);
        NotifyStateChanged();
    }

    private async Task ReloadMilestonesAsync(CancellationToken cancellationToken)
    {
        Milestones.Clear();

        if (Cook is null)
        {
            return;
        }

        var events = await _cooks.GetEventsAsync(Cook.Id, cancellationToken);
        foreach (var milestone in events)
        {
            Milestones.Add(new EventDisplay(
                milestone.RecordedAt,
                TimeZoneInfo.ConvertTime(milestone.RecordedAt, _clock.LocalTimeZone),
                EnumDisplay.KeyFor(milestone.Type),
                milestone.Note));
        }
    }

    /// <summary>
    /// Opens the start-a-cook form. Lives here because the empty state — no cook
    /// running — is this screen, and starting one is the only thing to do from it.
    /// </summary>
    [RelayCommand]
    private Task StartANewCookAsync(CancellationToken cancellationToken)
        => _navigation.GoToAsync(AppRoutes.StartCook, cancellationToken);

    /// <summary>Loads the running cook, if there is one, and its readings.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Cook = await _cooks.GetActiveCookAsync(cancellationToken);
        await ReloadEntriesAsync(cancellationToken);
        await ReloadMilestonesAsync(cancellationToken);
        NotifyStateChanged();
    }

    /// <summary>
    /// Records a reading from what is on the sheet. Both temperatures arrive in
    /// the user's unit and are converted to Celsius here; nothing below this line
    /// sees Fahrenheit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLogTemperature))]
    private async Task LogTemperatureAsync(CancellationToken cancellationToken)
    {
        if (Cook is null)
        {
            throw new InvalidOperationException("There is no cook in progress to log against.");
        }

        var unit = _settings.TemperatureUnit;

        await _cooks.LogTemperatureAsync(
            new LogTemperatureRequest
            {
                CookId = Cook.Id,
                MeatTempC = UnitConversion.ToCelsius(MeatTempInput!.Value, unit),
                PitTempC = PitTempInput is { } pit ? UnitConversion.ToCelsius(pit, unit) : null,
                RecordedAt = ResolveRecordedAt(),
                Note = string.IsNullOrWhiteSpace(NoteInput) ? null : NoteInput.Trim(),
            },
            cancellationToken);

        ClearSheet();
        await ReloadEntriesAsync(cancellationToken);
        NotifyStateChanged();
    }

    /// <summary>
    /// The instant to store. Null means "now" and is resolved by the service, so
    /// the default path does no timezone arithmetic at all. A custom time is
    /// wall-clock time in the user's zone and is converted to an instant here —
    /// the display boundary — because everything below stores UTC.
    /// </summary>
    private DateTimeOffset? ResolveRecordedAt()
    {
        if (!UseCustomTime)
        {
            return null;
        }

        var wallClock = RecordedDate.Date + RecordedTime;
        var offset = _clock.LocalTimeZone.GetUtcOffset(wallClock);

        return new DateTimeOffset(wallClock, offset).ToUniversalTime();
    }

    /// <summary>
    /// Resets the sheet after a successful log. The next reading starts from an
    /// empty box and "now", so a tired cook cannot log the same number twice by
    /// tapping again.
    /// </summary>
    private void ClearSheet()
    {
        MeatTempInput = null;
        PitTempInput = null;
        NoteInput = null;
        UseCustomTime = false;
        SeedRecordedAtWithNow();
    }

    private void SeedRecordedAtWithNow()
    {
        var localNow = TimeZoneInfo.ConvertTime(_clock.UtcNow, _clock.LocalTimeZone);

        RecordedDate = localNow.Date;
        RecordedTime = localNow.TimeOfDay;
    }

    partial void OnMeatTempInputChanged(double? value)
    {
        OnPropertyChanged(nameof(CanLogTemperature));
        LogTemperatureCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasActiveCook))]
    private async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (Cook is null)
        {
            throw new InvalidOperationException("There is no cook in progress to finish.");
        }

        Cook = await _cooks.FinishCookAsync(Cook.Id, cancellationToken: cancellationToken);
        NotifyStateChanged();
    }

    private async Task ReloadEntriesAsync(CancellationToken cancellationToken)
    {
        Entries.Clear();
        LastMeatTemp = null;

        if (Cook is null)
        {
            return;
        }

        var entries = await _cooks.GetTemperaturesAsync(Cook.Id, cancellationToken);
        var unit = _settings.TemperatureUnit;
        foreach (var entry in entries)
        {
            Entries.Add(new TempEntryDisplay(
                entry.RecordedAt,
                TimeZoneInfo.ConvertTime(entry.RecordedAt, _clock.LocalTimeZone),
                UnitConversion.FromCelsius(entry.MeatTempC, unit),
                entry.Note));
        }

        // "Last" means the latest reading by when it was taken, which is not
        // necessarily the one entered most recently: a back-dated entry must not
        // become the headline number.
        if (entries.Count > 0)
        {
            var latest = entries.MaxBy(e => e.RecordedAt)!;
            LastMeatTemp = UnitConversion.FromCelsius(latest.MeatTempC, unit);
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveCook));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsCookLoaded));
        OnPropertyChanged(nameof(CanLogTemperature));
        OnPropertyChanged(nameof(HasReadings));
        OnPropertyChanged(nameof(HasMilestones));
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(ElapsedDisplay));
        OnPropertyChanged(nameof(TemperatureUnitSymbol));

        LogTemperatureCommand.NotifyCanExecuteChanged();
        LogMilestoneCommand.NotifyCanExecuteChanged();
        AddFuelCommand.NotifyCanExecuteChanged();
        FinishCommand.NotifyCanExecuteChanged();
    }
}
