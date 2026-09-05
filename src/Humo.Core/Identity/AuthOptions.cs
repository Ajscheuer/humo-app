namespace Humo.Core.Identity;

/// <summary>
/// The tenant this build signs in against.
/// <para>
/// None of these are secrets — a public mobile client has no client secret, which
/// is why the flow is authorization code with PKCE — but they are per-environment
/// and are supplied at build or run time rather than committed.
/// </para>
/// <para>
/// A checkout with no tenant configured is a normal state, not a broken one:
/// <see cref="IsConfigured"/> is false, the sign-in screen says so, and
/// "continue without an account" still works end to end.
/// </para>
/// </summary>
public sealed record AuthOptions
{
    /// <summary>The application (client) ID registered in Entra External ID.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The authority URL for the tenant and user flow, e.g.
    /// <c>https://humo.ciamlogin.com/humo.onmicrosoft.com</c>.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>
    /// Where the browser returns to. Must match the platform's registered
    /// redirect URI exactly, or the provider refuses the round trip.
    /// </summary>
    public string? RedirectUri { get; init; }

    /// <summary>
    /// Scopes requested for the Humo API. Empty until the API is registered,
    /// which is slice 5.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Whether this build can actually sign anyone in. Checked before any
    /// provider call, so an unconfigured build fails with a clear outcome rather
    /// than an exception out of the identity library.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}
