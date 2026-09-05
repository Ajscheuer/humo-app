using Humo.Core.Navigation;

namespace Humo.App.Services;

/// <summary>
/// <see cref="INavigationService"/> over Shell. The only place a ViewModel's
/// intent to change screens meets a MAUI type.
/// </summary>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route, CancellationToken cancellationToken = default)
        => Shell.Current.GoToAsync(route);

    public Task GoBackAsync(CancellationToken cancellationToken = default)
        => Shell.Current.GoToAsync("..");
}
