using Humo.Core.ViewModels;

namespace Humo.App.Views;

public partial class EquipmentListPage : ContentPage
{
    // Constructor and InitializeComponent only. No logic in code-behind — see
    // CLAUDE.md. Behaviors, converters and commands cover the rest.
    public EquipmentListPage(EquipmentListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
