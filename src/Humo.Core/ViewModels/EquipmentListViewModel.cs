using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Shared.Entities;

namespace Humo.Core.ViewModels;

/// <summary>One rig as the list shows it: its name, and its type as a resource key.</summary>
public sealed record EquipmentListItem(Guid Id, string Name, string TypeKey);

/// <summary>The list of rigs, and the entry point to adding or editing one.</summary>
public sealed partial class EquipmentListViewModel : ObservableObject
{
    private readonly IEquipmentService _equipment;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public EquipmentListViewModel(
        IEquipmentService equipment,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _equipment = equipment;
        _localizer = localizer;
        _navigation = navigation;
    }

    public ObservableCollection<EquipmentListItem> Items { get; } = [];

    public string Title => _localizer[AppStrings.Equipment_Title];

    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// Set when a delete was refused, so the page can say why rather than
    /// appearing to ignore the tap.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var equipment = await _equipment.GetAllAsync(cancellationToken);

        Items.Clear();
        foreach (var rig in equipment)
        {
            Items.Add(new EquipmentListItem(rig.Id, rig.Name, EnumDisplay.KeyFor(rig.Type)));
        }

        ErrorMessage = null;
        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private Task AddAsync(CancellationToken cancellationToken)
        => _navigation.GoToAsync(AppRoutes.EditEquipment, cancellationToken);

    [RelayCommand]
    private Task EditAsync(EquipmentListItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _navigation.GoToAsync(
            AppRoutes.EditEquipmentFor(item.Id), cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteAsync(EquipmentListItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            await _equipment.DeleteAsync(item.Id, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The one refusal a user can actually hit: a cook is running on this
            // rig. Surfaced as a message rather than an exception, because it is
            // a normal thing to try and the app must not fall over on it.
            ErrorMessage = _localizer[AppStrings.Equipment_InUse];
            return;
        }

        await LoadAsync(cancellationToken);
    }
}
