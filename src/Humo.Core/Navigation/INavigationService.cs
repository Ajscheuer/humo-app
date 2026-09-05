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

    /// <summary>The list of rigs.</summary>
    public const string Equipment = "equipment";

    /// <summary>The add/edit rig form. Pushed, so Back cancels the edit.</summary>
    public const string EditEquipment = "editequipment";

    /// <summary>The fuel sheet. Pushed over the cook screen.</summary>
    public const string FuelSheet = "fuel";

    /// <summary>Query-string key naming the rig being edited or fed.</summary>
    public const string EquipmentIdParameter = "equipmentId";

    /// <summary>Query-string key naming the cook that was on screen.</summary>
    public const string CookIdParameter = "cookId";

    /// <summary>First launch: sign in, or continue as a guest.</summary>
    public const string SignIn = "signin";

    /// <summary>The list of finished cooks.</summary>
    public const string History = "history";

    /// <summary>One finished cook's chart and statistics. Pushed from the history list.</summary>
    public const string CookSummary = "summary";

    /// <summary>The summary screen for one cook.</summary>
    public static string CookSummaryFor(Guid cookId)
        => $"{CookSummary}?{CookIdParameter}={cookId}";

    /// <summary>
    /// The fuel sheet for a rig, optionally noting the cook on screen. The cook
    /// is display-only: fuel belongs to the fire, so the sheet works with no cook
    /// running at all — which is exactly the case when bringing a pit up to heat.
    /// </summary>
    public static string FuelSheetFor(Guid equipmentId, Guid? cookId = null)
    {
        var route = $"{FuelSheet}?{EquipmentIdParameter}={equipmentId}";
        return cookId is { } id ? $"{route}&{CookIdParameter}={id}" : route;
    }

    /// <summary>
    /// The edit form for an existing rig. Built here rather than at each call
    /// site so the parameter name cannot drift between the page that reads it
    /// and the ViewModels that navigate to it.
    /// </summary>
    public static string EditEquipmentFor(Guid equipmentId)
        => $"{EditEquipment}?{EquipmentIdParameter}={equipmentId}";
}
