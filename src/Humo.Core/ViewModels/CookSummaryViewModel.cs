using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Analytics;
using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Shared.Units;

namespace Humo.Core.ViewModels;

/// <summary>
/// One finished cook: what it added up to, and the chart of how it went.
/// <para>
/// Everything here is computed from this cook alone. Comparison against the
/// user's own baseline is cross-cook, server-computed and Pro-gated, and is
/// deliberately absent — which is what keeps this screen working offline.
/// </para>
/// </summary>
public sealed partial class CookSummaryViewModel : ObservableObject
{
    private readonly ICookSummaryService _summaries;
    private readonly IUserSettings _settings;
    private readonly ILocalizer _localizer;

    public CookSummaryViewModel(
        ICookSummaryService summaries,
        IUserSettings settings,
        ILocalizer localizer)
    {
        _summaries = summaries;
        _settings = settings;
        _localizer = localizer;
    }

    [ObservableProperty]
    private CookSummary? _summary;

    /// <summary>Set when the cook could not be loaded, so the page can say why.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public string Title => _localizer[AppStrings.Summary_Title];

    public bool HasSummary => Summary is not null;

    /// <summary>
    /// True when the end time was inferred rather than observed. The page shows a
    /// note: a user must never be quietly handed a guess as a measurement.
    /// </summary>
    public bool IsEstimated => Summary?.Statistics.IsEstimated ?? false;

    public string MeatTypeKey => Summary is null
        ? AppStrings.Summary_Unknown
        : EnumDisplay.KeyFor(Summary.Cook.MeatType);

    /// <summary>
    /// The rig's name, or a note that it has been deleted. Not localized when it
    /// is a name: that is user data, and it reads the same in both languages.
    /// </summary>
    public string EquipmentName => Summary?.Equipment?.Name
                                   ?? _localizer[AppStrings.Summary_RigDeleted];

    public string DurationDisplay => FormatDuration(Summary?.Statistics.Duration);

    /// <summary>
    /// Time per unit of weight, in the user's weight unit.
    /// <para>
    /// <see cref="CookStatistics.TimePerKg"/> is per kilogram because storage is
    /// metric and trends must be comparable. A cook who thinks in pounds needs
    /// the same figure per pound, so the division happens here against the
    /// displayed weight rather than being read straight off the statistics.
    /// </para>
    /// </summary>
    public string TimePerWeightDisplay
    {
        get
        {
            if (Summary?.Statistics.Duration is not { } duration)
            {
                return _localizer[AppStrings.Summary_Unknown];
            }

            var displayWeight = UnitConversion.FromKilograms(
                Summary.Cook.WeightKg, _settings.WeightUnit);

            return displayWeight > 0 && double.IsFinite(displayWeight)
                ? FormatDuration(duration / displayWeight)
                : _localizer[AppStrings.Summary_Unknown];
        }
    }

    /// <summary>
    /// The label beside <see cref="TimePerWeightDisplay"/>: "time per kilo" or
    /// "time per pound", so the number is never labelled with the wrong unit.
    /// </summary>
    public string TimePerWeightLabelKey => _settings.WeightUnit == WeightUnit.Pounds
        ? AppStrings.Summary_TimePerLb
        : AppStrings.Summary_TimePerKg;

    public string PeakMeatTempDisplay => FormatTemperature(Summary?.Statistics.PeakMeatTempC);

    public string PeakPitTempDisplay => FormatTemperature(Summary?.Statistics.PeakPitTempC);

    public string ReadingCountDisplay => FormatCount(Summary?.Statistics.ReadingCount);

    public string FuelLoadCountDisplay => FormatCount(Summary?.Statistics.FuelEventCount);

    public string TemperatureUnitSymbol => _localizer[
        _settings.TemperatureUnit == TemperatureUnit.Fahrenheit
            ? AppStrings.Unit_Fahrenheit_Short
            : AppStrings.Unit_Celsius_Short];

    /// <summary>The chart, as plain data. Empty rather than null when there is nothing to draw.</summary>
    public CookChartData Chart => Summary?.Chart ?? CookChartData.Empty(
        _settings.TemperatureUnit == TemperatureUnit.Fahrenheit
            ? AppStrings.Unit_Fahrenheit_Short
            : AppStrings.Unit_Celsius_Short);

    public bool HasChart => !Chart.IsEmpty;

    [RelayCommand]
    private async Task LoadAsync(Guid cookId, CancellationToken cancellationToken)
    {
        Summary = await _summaries.GetSummaryAsync(cookId, cancellationToken);

        // Deleted on another device between the list loading and this tap. The
        // page says so rather than showing a screen of em dashes.
        ErrorMessage = Summary is null ? _localizer[AppStrings.Summary_NotFound] : null;

        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(IsEstimated));
        OnPropertyChanged(nameof(MeatTypeKey));
        OnPropertyChanged(nameof(EquipmentName));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(TimePerWeightDisplay));
        OnPropertyChanged(nameof(TimePerWeightLabelKey));
        OnPropertyChanged(nameof(PeakMeatTempDisplay));
        OnPropertyChanged(nameof(PeakPitTempDisplay));
        OnPropertyChanged(nameof(ReadingCountDisplay));
        OnPropertyChanged(nameof(FuelLoadCountDisplay));
        OnPropertyChanged(nameof(TemperatureUnitSymbol));
        OnPropertyChanged(nameof(Chart));
        OnPropertyChanged(nameof(HasChart));
    }

    /// <summary>Hours and minutes, or an em dash. Total hours, so a 26-hour cook reads 26:10.</summary>
    private string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } elapsed)
        {
            return _localizer[AppStrings.Summary_Unknown];
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}");
    }

    /// <summary>
    /// A stored Celsius reading in the user's unit, with its symbol. Converted
    /// here, at the display boundary, and nowhere below it.
    /// </summary>
    private string FormatTemperature(double? celsius)
    {
        if (celsius is not { } value)
        {
            return _localizer[AppStrings.Summary_Unknown];
        }

        var display = UnitConversion.FromCelsius(value, _settings.TemperatureUnit);

        // The number follows the culture -- Spanish uses a decimal comma -- while
        // the unit symbol does not follow the language.
        return string.Format(
            _localizer.CurrentCulture, "{0:0.#}{1}", display, TemperatureUnitSymbol);
    }

    private string FormatCount(int? count) => count is { } value
        ? value.ToString(_localizer.CurrentCulture)
        : _localizer[AppStrings.Summary_Unknown];
}
