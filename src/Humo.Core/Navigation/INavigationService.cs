namespace Humo.Core.Navigation;

/// <summary>
/// Moving between screens, as a ViewModel sees it.
/// <para>
/// ViewModels depend on this rather than on Shell, so "starting a cook takes you
/// to the cook screen" is a behaviour a unit test can assert instead of something
/// only a device can demonstrate.
/// </para>
/// </summary>
public interface INavigationService
{
    /// <summary>Navigates to a route from <see cref="AppRoutes"/>.</summary>
    Task GoToAsync(string route, CancellationToken cancellationToken = default);

    Task GoBackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Route names, shared between the ViewModels that navigate and the app that
/// registers them. A typo here is a failing test rather than a dead button.
/// </summary>
public static class AppRoutes
{
    /// <summary>
    /// The cook screen, as an absolute route: going here resets the stack rather
    /// than pushing a second copy on top of the one already underneath. Finishing
    /// the start form should leave nothing to press Back into.
    /// </summary>
    public const string ActiveCook = "//activecook";

    /// <summary>Pushed on top of wherever the user is, so Back cancels it.</summary>
    public const string StartCook = "startcook";

    public const string Settings = "//settings";
}
