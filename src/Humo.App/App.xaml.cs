using Humo.App.Services;

namespace Humo.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    // Resolved rather than constructed: AppShell needs IAccountService to decide
    // whether this launch shows the sign-in screen.
    protected override Window CreateWindow(IActivationState? activationState)
        => new(ServiceHelper.GetRequiredService<AppShell>());
}
