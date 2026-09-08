using Humo.Core.ViewModels;

namespace Humo.App.Views;

public partial class SignInPage : ContentPage
{
    // Constructor and InitializeComponent only. No logic in code-behind — see
    // CLAUDE.md. Behaviors, converters and commands cover the rest.
    public SignInPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
