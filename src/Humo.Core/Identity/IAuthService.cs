namespace Humo.Core.Identity;

/// <summary>How a user proved who they are.</summary>
public enum SignInMethod
{
    /// <summary>No sign-in. A device-local account, the "continue without an account" path.</summary>
    Anonymous = 0,

    Apple = 1,
    Google = 2,
    Email = 3,
}

/// <summary>
/// A signed-in identity as the app sees it.
/// <para>
/// Deliberately provider-agnostic: nothing above <see cref="IAuthService"/> knows
/// that Entra External ID is behind it, which is what lets the sign-in flow
/// change without touching feature code.
/// </para>
/// </summary>
public sealed record AuthenticatedUser
{
    /// <summary>
    /// The provider's stable subject identifier. Not the account id — the mapping
    /// from subject to account is what makes claiming a guest's data possible.
    /// </summary>
    public required string Subject { get; init; }

    public required SignInMethod Method { get; init; }

    public string? Email { get; init; }

    public string? DisplayName { get; init; }
}

/// <summary>Why a sign-in did not produce a user.</summary>
public enum SignInOutcome
{
    Succeeded = 0,

    /// <summary>The user backed out of the provider's screen. Not an error.</summary>
    Cancelled = 1,

    /// <summary>Offline, or the provider was unreachable.</summary>
    NetworkUnavailable = 2,

    /// <summary>No tenant configured in this build. See <c>AuthOptions</c>.</summary>
    NotConfigured = 3,

    Failed = 4,
}

/// <summary>The result of asking the user to sign in.</summary>
public sealed record SignInResult
{
    public required SignInOutcome Outcome { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is Succeeded.</summary>
    public AuthenticatedUser? User { get; init; }

    public bool IsSuccess => Outcome == SignInOutcome.Succeeded && User is not null;

    public static SignInResult Success(AuthenticatedUser user) =>
        new() { Outcome = SignInOutcome.Succeeded, User = user };

    public static SignInResult Failure(SignInOutcome outcome) => new() { Outcome = outcome };
}

/// <summary>
/// Signing in and out, behind an interface so the provider is swappable.
/// <para>
/// The implementation is in <c>Humo.App</c> because it needs a browser and the
/// platform's secure storage. Everything here is provider-neutral, so the choice
/// between Entra's hosted user flow and native SDK flows is a change on one side
/// of this line only.
/// </para>
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Whether this build has a tenant configured. False in a checkout with no
    /// tenant values, which is why the sign-in screen must degrade rather than
    /// present buttons that cannot work.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Presents the provider's sign-in and returns who signed in. Never throws
    /// for a cancelled or offline attempt — those are outcomes, not faults, and
    /// a user backing out of a web view is the single most common one.
    /// </summary>
    Task<SignInResult> SignInAsync(SignInMethod method, CancellationToken cancellationToken = default);

    /// <summary>Clears the stored tokens. Local data is untouched.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A bearer token for the API, refreshed if needed, or null when there is
    /// none. Guests always get null; they cannot sync, which is the point.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
