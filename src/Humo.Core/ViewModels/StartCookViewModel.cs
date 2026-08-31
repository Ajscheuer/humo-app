using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Shared;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;

namespace Humo.Core.ViewModels;

/// <summary>A meat type as the picker shows it: a stored value plus its resource key.</summary>
public sealed record MeatTypeOption(MeatType Value, string DisplayNameKey);

/// <summary>
/// Starting a cook.
/// <para>
/// Everything the user types is in <em>their</em> units; everything this hands to
/// <see cref="ICookService"/> is Celsius and kilograms. That conversion happens
/// here, at the display boundary, and nowhere else.
/// </para>
/// </summary>
public sealed partial class StartCookViewModel : ObservableObject
{
    private readonly ICookService _cooks;
    private readonly IUserSettings _settings;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public StartCookViewModel(
        ICookService cooks,
        IUserSettings settings,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _cooks = cooks;
        _settings = settings;
        _localizer = localizer;
        _navigation = navigation;

        MeatTypes = EnumDisplay.MeatTypesInDisplayOrder
            .Select(t => new MeatTypeOption(t, EnumDisplay.KeyFor(t)))
            .ToList();

        _selectedMeatType = MeatTypes[0];
        _weight = DisplayWeightFor(MeatTypes[0].Value);
    }

    public IReadOnlyList<MeatTypeOption> MeatTypes { get; }

    public string Title => _localizer[AppStrings.StartCook_Title];

    /// <summary>Unit symbol for the weight field, in the user's preferred unit.</summary>
    public string WeightUnitSymbol => _localizer[
        _settings.WeightUnit == WeightUnit.Pounds
            ? AppStrings.Unit_Pounds_Short
            : AppStrings.Unit_Kilograms_Short];

    /// <summary>Unit symbol for the target temperature field.</summary>
    public string TemperatureUnitSymbol => _localizer[
        _settings.TemperatureUnit == TemperatureUnit.Fahrenheit
            ? AppStrings.Unit_Fahrenheit_Short
            : AppStrings.Unit_Celsius_Short];

    [ObservableProperty]
    private MeatTypeOption _selectedMeatType;

    /// <summary>Free text, shown only when the selected meat type is Other.</summary>
    [ObservableProperty]
    private string? _meatTypeOther;

    /// <summary>Weight in the user's unit — kilograms or pounds, never both.</summary>
    [ObservableProperty]
    private double _weight;

    /// <summary>Target internal temperature in the user's unit. Null is valid.</summary>
    [ObservableProperty]
    private double? _targetTemperature;

    public bool IsOtherMeatType => SelectedMeatType.Value == MeatType.Other;

    /// <summary>Weight must be positive; everything else may be left alone.</summary>
    public bool CanStart => double.IsFinite(Weight) && Weight > 0;

    partial void OnSelectedMeatTypeChanged(MeatTypeOption value)
    {
        // Re-seed the weight from the new meat type. Deliberately unconditional:
        // picking pork ribs after brisket should not leave 6 kg sitting in the
        // box. The user's own edit survives only until they change meat type,
        // which is the moment the old number stopped being relevant.
        Weight = DisplayWeightFor(value.Value);

        if (value.Value != MeatType.Other)
        {
            MeatTypeOther = null;
        }

        OnPropertyChanged(nameof(IsOtherMeatType));
    }

    partial void OnWeightChanged(double value) => StartCookCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartCookAsync(CancellationToken cancellationToken)
    {
        var request = new StartCookRequest
        {
            MeatType = SelectedMeatType.Value,
            MeatTypeOther = IsOtherMeatType && !string.IsNullOrWhiteSpace(MeatTypeOther)
                ? MeatTypeOther.Trim()
                : null,

            // Into storage units, once, here.
            WeightKg = UnitConversion.ToKilograms(Weight, _settings.WeightUnit),
            TargetInternalTempC = TargetTemperature is { } target
                ? UnitConversion.ToCelsius(target, _settings.TemperatureUnit)
                : null,
        };

        await _cooks.StartCookAsync(request, cancellationToken).ConfigureAwait(false);

        // The cook is on. The next thing the user wants is the cook screen, not
        // the form they just filled in.
        await _navigation.GoToAsync(AppRoutes.ActiveCook, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The meat type's typical weight, expressed in the user's unit.</summary>
    private double DisplayWeightFor(MeatType meatType)
    {
        var kilograms = MeatTypeDefaults.ForMeatType(meatType);
        var display = UnitConversion.FromKilograms(kilograms, _settings.WeightUnit);

        // Round for display only. Storage keeps whatever the conversion produced
        // from whatever the user finally enters.
        return Math.Round(display, 1);
    }
}
