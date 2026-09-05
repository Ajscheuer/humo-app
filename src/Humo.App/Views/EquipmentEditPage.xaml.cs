using Humo.Core.Navigation;
using Humo.Core.ViewModels;

namespace Humo.App.Views;

/// <summary>
/// The add/edit rig form.
/// <para>
/// It takes the rig id from the route's query string. That plumbing is the one
/// thing Shell cannot express in XAML, so it lives here as a property setter that
/// forwards straight to the ViewModel's load command — no decisions, no logic.
/// </para>
/// </summary>
[QueryProperty(nameof(EquipmentId), AppRoutes.EquipmentIdParameter)]
public partial class EquipmentEditPage : ContentPage
{
    private readonly EquipmentEditViewModel _viewModel;

    public EquipmentEditPage(EquipmentEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>
    /// Set by Shell from the route. Absent or unparseable means "add a rig",
    /// which is also what a stale link should degrade to rather than an error.
    /// </summary>
    public string? EquipmentId
    {
        set => _viewModel.LoadCommand.Execute(
            Guid.TryParse(value, out var id) ? id : (Guid?)null);
    }
}
