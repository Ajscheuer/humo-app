using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Shared.Enums;
using Humo.Shared.Units;

namespace Humo.Core.ViewModels;

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record WoodTypeOption(WoodType Value, string DisplayNameKey);

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record FuelFormOption(FuelForm Value, string DisplayNameKey);

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record SizeClassOption(SizeClass Value, string DisplayNameKey);

/// <summary>
/// The fuel sheet.
/// <para>
/// One requirement dominates this class: logging fuel must take <b>two taps</b>.
/// A cook adding a split is standing at an open firebox door with a glove on, at
/// night. So the sheet opens with wood type and form already filled in from the
/// last load on this rig, and the only thing left is a single size tap that
/// commits immediately. Count, weight and the wood pickers exist on the same
/// sheet but are never on that path.
/// </para>
/// </summary>
public sealed partial class FuelSheetViewModel : ObservableObject
{
    private readonly IFuelService _fuel;
    private readonly IUserSettings _settings;
    private readonly ILocalizer _localizer;

    private Guid _equipmentId;
    private Guid? _cookId;

    public FuelSheetViewModel(IFuelService fuel, IUserSettings settings, ILocalizer localizer)
    {
        _fuel = fuel;
        _settings = settings;
        _localizer = localizer;

        WoodTypes = EnumDisplay.WoodTypesInDisplayOrder
            .Select(t => new WoodTypeOption(t, EnumDisplay.KeyFor(t)))
            .ToList();

        FuelForms = EnumDisplay.FuelFormsInDisplayOrder
            .Select(f => new FuelFormOption(f, EnumDisplay.KeyFor(f)))
            .ToList();

        SizeClasses = EnumDisplay.SizeClassesInDisplayOrder
            .Select(s => new SizeClassOption(s, EnumDisplay.KeyFor(s)))
            .ToList();

        _selectedWoodType = WoodTypes[0];
        _selectedForm = FuelForms[0];
    }

    public IReadOnlyList<WoodTypeOption> WoodTypes { get; }

    public IReadOnlyList<FuelFormOption> FuelForms { get; }

    /// <summary>Small, medium, large — the three buttons on the fast path.</summary>
    public IReadOnlyList<SizeClassOption> SizeClasses { get; }

    public string Title => _localizer[AppStrings.Fuel_Title];

    public string WeightUnitSymbol => _localizer[
        _settings.WeightUnit == WeightUnit.Pounds
            ? AppStrings.Unit_Pounds_Short
            : AppStrings.Unit_Kilograms_Short];

    [ObservableProperty]
    private WoodTypeOption _selectedWoodType;

    /// <summary>Free text, shown only when the wood type is Other.</summary>
    [ObservableProperty]
    private string? _woodTypeOther;

    [ObservableProperty]
    private FuelFormOption _selectedForm;

    /// <summary>Pieces. Defaults to one and is never on the fast path.</summary>
    [ObservableProperty]
    private int _count = 1;

    /// <summary>Optional, in the user's unit.</summary>
    [ObservableProperty]
    private double? _weight;

    /// <summary>
    /// True when the pre-fill came from a real previous load rather than the
    /// cold-start fallback. The sheet uses it to decide whether to say "same as
    /// last time" or to draw attention to the wood pickers.
    /// </summary>
    [ObservableProperty]
    private bool _prefilledFromLastLoad;

    public bool IsOtherWoodType => SelectedWoodType.Value == WoodType.Other;

    partial void OnSelectedWoodTypeChanged(WoodTypeOption value)
    {
        if (value.Value != WoodType.Other)
        {
            WoodTypeOther = null;
        }

        OnPropertyChanged(nameof(IsOtherWoodType));
    }

    /// <summary>
    /// Opens the sheet for a rig, pre-filling from that rig's last load.
    /// <para>
    /// The rig, not the cook: a firebox does not know how many pieces of meat
    /// are above it, so two cooks on one smoker share one fuel history and the
    /// sheet never has to ask which cook this is for.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task PrepareAsync(FuelSheetContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _equipmentId = context.EquipmentId;
        _cookId = context.CookId;

        var defaults = await _fuel.GetDefaultsAsync(_equipmentId, cancellationToken)
            .ConfigureAwait(false);

        SelectedWoodType = WoodTypes.First(o => o.Value == defaults.WoodType);
        WoodTypeOther = defaults.WoodTypeOther;
        SelectedForm = FuelForms.First(o => o.Value == defaults.Form);
        PrefilledFromLastLoad = defaults.FromPreviousEvent;

        // Deliberately reset rather than carried forward. Size is the judgement
        // the cook makes each time, and a remembered count would silently log
        // three splits when they added one.
        Count = 1;
        Weight = null;
    }

    /// <summary>
    /// Tap two: commits the event immediately with the size just chosen.
    /// <para>
    /// There is no confirm step on purpose. A sheet that asked "are you sure?"
    /// would be three taps, and a wrong size class is a far smaller problem than
    /// a cook who stops logging fuel because logging fuel is annoying.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task LogSizeAsync(SizeClass sizeClass, CancellationToken cancellationToken)
    {
        if (_equipmentId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The fuel sheet was not prepared with a rig. Call PrepareCommand first.");
        }

        await _fuel.LogFuelAsync(
            new LogFuelRequest
            {
                EquipmentId = _equipmentId,
                CookId = _cookId,
                WoodType = SelectedWoodType.Value,
                WoodTypeOther = IsOtherWoodType ? WoodTypeOther : null,
                Form = SelectedForm.Value,
                SizeClass = sizeClass,
                Count = Count,

                // Into storage units, once, here.
                WeightKg = Weight is { } weight
                    ? UnitConversion.ToKilograms(weight, _settings.WeightUnit)
                    : null,
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// What the sheet needs to know before it opens: the fire being fed, and the
/// cook that happened to be on screen.
/// </summary>
public sealed record FuelSheetContext(Guid EquipmentId, Guid? CookId);
