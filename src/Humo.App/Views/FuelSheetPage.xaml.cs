using Humo.Core.Navigation;
using Humo.Core.ViewModels;

namespace Humo.App.Views;

/// <summary>
/// The fuel sheet. Opened with the rig being fed and the cook on screen, both
/// arriving as route parameters — the only code-behind here forwards them to the
/// ViewModel's prepare command.
/// </summary>
[QueryProperty(nameof(EquipmentId), AppRoutes.EquipmentIdParameter)]
[QueryProperty(nameof(CookId), AppRoutes.CookIdParameter)]
public partial class FuelSheetPage : ContentPage
{
    private readonly FuelSheetViewModel _viewModel;
    private Guid _equipmentId;

    public FuelSheetPage(FuelSheetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>The fire being fed. Required; without it there is nothing to log against.</summary>
    public string? EquipmentId
    {
        set
        {
            _equipmentId = Guid.TryParse(value, out var id) ? id : Guid.Empty;
            Prepare();
        }
    }

    /// <summary>The cook on screen, recorded for display. Optional.</summary>
    public string? CookId
    {
        set
        {
            _cookId = Guid.TryParse(value, out var id) ? id : null;
            Prepare();
        }
    }

    private Guid? _cookId;

    // Shell sets query properties one at a time and in no guaranteed order, so
    // this runs on each and no-ops until the rig -- the one required value -- has
    // arrived. Preparing twice is harmless; preparing without a rig is not.
    private void Prepare()
    {
        if (_equipmentId != Guid.Empty)
        {
            _viewModel.PrepareCommand.Execute(new FuelSheetContext(_equipmentId, _cookId));
        }
    }
}
