using Humo.Core.ViewModels;

namespace Humo.App.Views;

public partial class PaywallPage : ContentPage
{
    // Constructor and InitializeComponent only. No logic in code-behind — see
    // CLAUDE.md. Behaviors, converters and commands cover the rest.
    public PaywallPage(PaywallViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
