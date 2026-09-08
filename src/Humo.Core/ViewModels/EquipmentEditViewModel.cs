using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Shared.Enums;

namespace Humo.Core.ViewModels;

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record EquipmentTypeOption(EquipmentType Value, string DisplayNameKey);

/// <summary>An enum value paired with the resource key that displays it.</summary>
public sealed record InsulationOption(InsulationLevel Value, string DisplayNameKey);

/// <summary>
/// Adding or editing a rig.
/// <para>
/// Volumes are litres in storage and litres on screen — unlike temperature and
/// weight, Humo has no second unit for them, so nothing is converted here. If a
/// gallons setting ever arrives, this is where the conversion goes.
/// </para>
/// </summary>
public sealed partial class EquipmentEditViewModel : ObservableObject
{
    private readonly IEquipmentService _equipment;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    private Guid? _editingId;

    public EquipmentEditViewModel(
        IEquipmentService equipment,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _equipment = equipment;
        _localizer = localizer;
        _navigation = navigation;

        EquipmentTypes = EnumDisplay.EquipmentTypesInDisplayOrder
            .Select(t => new EquipmentTypeOption(t, EnumDisplay.KeyFor(t)))
            .ToList();

        InsulationLevels = EnumDisplay.InsulationLevelsInDisplayOrder
            .Select(l => new InsulationOption(l, EnumDisplay.KeyFor(l)))
            .ToList();

        _selectedType = EquipmentTypes[0];
        _selectedInsulation = InsulationLevels[0];
    }

    public IReadOnlyList<EquipmentTypeOption> EquipmentTypes { get; }

    public IReadOnlyList<InsulationOption> InsulationLevels { get; }

    public string Title => _localizer[
        _editingId is null ? AppStrings.Equipment_Add : AppStrings.Equipment_Edit];

    public string VolumeUnitSymbol => _localizer[AppStrings.Unit_Litres_Short];

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private EquipmentTypeOption _selectedType;

    [ObservableProperty]
    private InsulationOption _selectedInsulation;

    /// <summary>Litres. Optional.</summary>
    [ObservableProperty]
    private double? _fireboxVolumeL;

    /// <summary>Litres. Optional.</summary>
    [ObservableProperty]
    private double? _cookChamberVolumeL;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// A rig needs a name; everything else has a default. The volumes are
    /// optional, but a volume that is present must be a positive number — an
    /// entry box makes "-5" and "0" reachable, and the fire model treats absent
    /// and zero as different things.
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Name)
                           && IsUsableVolume(FireboxVolumeL)
                           && IsUsableVolume(CookChamberVolumeL);

    private static bool IsUsableVolume(double? volume)
        => volume is null || (double.IsFinite(volume.Value) && volume.Value > 0);

    partial void OnNameChanged(string? value) => RefreshCanSave();

    partial void OnFireboxVolumeLChanged(double? value) => RefreshCanSave();

    partial void OnCookChamberVolumeLChanged(double? value) => RefreshCanSave();

    private void RefreshCanSave()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads an existing rig, or leaves the form empty for a new one.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(Guid? equipmentId, CancellationToken cancellationToken)
    {
        _editingId = equipmentId;
        ErrorMessage = null;

        if (equipmentId is not { } id)
        {
            OnPropertyChanged(nameof(Title));
            return;
        }

        var rig = await _equipment.GetAsync(id, cancellationToken);
        if (rig is null)
        {
            // Deleted on another device between the list loading and this tap.
            // Fall back to "add" rather than showing a form bound to nothing.
            _editingId = null;
            OnPropertyChanged(nameof(Title));
            return;
        }

        Name = rig.Name;

        // FirstOrDefault, not First: a rig stored by a newer version of the app
        // may carry a type this build has no option for, and a form that throws
        // is worse than one showing the fallback. EnumDisplay.KeyFor guards the
        // same way.
        SelectedType = EquipmentTypes.FirstOrDefault(o => o.Value == rig.Type)
                       ?? EquipmentTypes[0];
        SelectedInsulation = InsulationLevels.FirstOrDefault(o => o.Value == rig.Insulation)
                             ?? InsulationLevels[0];
        FireboxVolumeL = rig.FireboxVolumeL;
        CookChamberVolumeL = rig.CookChamberVolumeL;
        Notes = rig.Notes;

        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _equipment.SaveAsync(
                new SaveEquipmentRequest
                {
                    Id = _editingId,
                    Name = Name!,
                    Type = SelectedType.Value,
                    Insulation = SelectedInsulation.Value,
                    FireboxVolumeL = FireboxVolumeL,
                    CookChamberVolumeL = CookChamberVolumeL,
                    Notes = Notes,
                },
                cancellationToken);
        }
        catch (ArgumentException)
        {
            // A name of nothing but spaces passes CanSave's IsNullOrWhiteSpace
            // check only if it changes underneath us; the service is the one
            // authority on what a valid rig is, so its refusal is what shows.
            // ArgumentOutOfRangeException for a volume lands here too, which is
            // why CanSave screens volumes rather than leaving them to this catch.
            ErrorMessage = _localizer[AppStrings.Equipment_NameRequired];
            return;
        }
        catch (InvalidOperationException)
        {
            // The rig was deleted between this form loading and Save being
            // tapped. LoadAsync already degrades to "add" for this race; without
            // the matching catch here the save path crashed on it instead.
            ErrorMessage = _localizer[AppStrings.Equipment_Gone];
            _editingId = null;
            OnPropertyChanged(nameof(Title));
            return;
        }

        await _navigation.GoBackAsync(cancellationToken);
    }
}
