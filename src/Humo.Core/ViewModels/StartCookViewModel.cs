using System.Collections.ObjectModel;
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
    private readonly IEquipmentService _equipment;
    private readonly IUserSettings _settings;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public StartCookViewModel(
        ICookService cooks,
        IEquipmentService equipment,
        IUserSettings settings,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _cooks = cooks;
        _equipment = equipment;
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

    /// <summary>
    /// The rigs to choose between. A cook must be attached to one, because the
    /// fire model learns per rig and a cadence learned on an offset means nothing
    /// on a kamado.
    /// </summary>
    public ObservableCollection<EquipmentListItem> Equipment { get; } = [];

    [ObservableProperty]
    private EquipmentListItem? _selectedEquipment;

    /// <summary>
    /// Only worth showing the picker once there is a choice to make. With one
    /// rig the answer is forced, and a picker with a single entry is a tap that
    /// buys nothing.
    /// </summary>
    public bool HasEquipmentChoice => Equipment.Count > 1;

    /// <summary>
    /// Loads the rigs and picks one. Creates the implicit default rig if the user
    /// has none, so starting a cook never dead-ends on "add equipment first".
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rigs = await _equipment.GetAllAsync(cancellationToken);

        if (rigs.Count == 0)
        {
            // No rigs yet: the service mints the implicit one rather than sending
            // the user off to a form before they can cook anything.
            var created = await _cooks.GetOrCreateDefaultEquipmentAsync(cancellationToken);
            rigs = [created];
        }

        Equipment.Clear();
        foreach (var rig in rigs)
        {
            Equipment.Add(new EquipmentListItem(rig.Id, rig.Name, EnumDisplay.KeyFor(rig.Type)));
        }

        SelectedEquipment = Equipment[0];
        OnPropertyChanged(nameof(HasEquipmentChoice));
        StartCookCommand.NotifyCanExecuteChanged();
    }

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

    /// <summary>
    /// Weight must be positive and a rig must be chosen; everything else may be
    /// left alone. The rig is guaranteed by LoadAsync, so this only fails if the
    /// form is somehow used before it loads.
    /// </summary>
    public bool CanStart => double.IsFinite(Weight) && Weight > 0 && SelectedEquipment is not null;

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

    partial void OnSelectedEquipmentChanged(EquipmentListItem? value)
        => StartCookCommand.NotifyCanExecuteChanged();

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
            EquipmentId = SelectedEquipment?.Id,
        };

        await _cooks.StartCookAsync(request, cancellationToken);

        // The cook is on. The next thing the user wants is the cook screen, not
        // the form they just filled in.
        await _navigation.GoToAsync(AppRoutes.ActiveCook, cancellationToken);
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
