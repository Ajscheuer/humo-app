using Humo.App.Services;
using Humo.Core.Sync;

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
    {
        var window = new Window(ServiceHelper.GetRequiredService<AppShell>());

        // Launch and resume are when a phone is most likely to have signal
        // again. ISyncTrigger returns immediately and never throws, so no user
        // action ever waits on this and a failed round cannot reach the UI.
        var sync = ServiceHelper.GetRequiredService<ISyncTrigger>();
        window.Created += (_, _) => sync.RequestSync();
        window.Resumed += (_, _) => sync.RequestSync();

        return window;
    }
}
