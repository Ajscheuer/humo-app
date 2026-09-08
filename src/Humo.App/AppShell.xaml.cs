using Humo.Core.Identity;
using Humo.Core.Navigation;

namespace Humo.App;

public partial class AppShell : Shell
{
    private readonly IAccountService _accounts;

    public AppShell(IAccountService accounts)
    {
        InitializeComponent();
        _accounts = accounts;
    }

    /// <summary>
    /// Sends a first launch to the sign-in screen.
    /// <para>
    /// Here rather than in XAML because Shell routes cannot be chosen
    /// conditionally in markup, and here rather than at startup because
    /// navigation needs a Shell that has finished appearing. It reads one flag
    /// and navigates; the decision itself belongs to <see cref="IAccountService"/>.
    /// </para>
    /// </summary>
    protected override async void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);

        if (!_accounts.NeedsSignInChoice)
        {
            return;
        }

        await GoToAsync(AppRoutes.SignIn);
    }
}
