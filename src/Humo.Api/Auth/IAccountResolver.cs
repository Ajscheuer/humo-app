using System.Security.Claims;

namespace Humo.Api.Auth;

/// <summary>
/// Whose account a request is acting on.
/// <para>
/// The single answer to "which account?" for the whole API, and it reads the
/// bearer token — never the request body. A client that puts another account's
/// id in a payload writes into its own. Behind an interface so a test can act as
/// a given account without minting real tokens, and so swapping the identity
/// provider does not reach into every endpoint.
/// </para>
/// </summary>
public interface IAccountResolver
{
    /// <summary>
    /// The account for this request, or null when the caller is unauthenticated
    /// or the token carries no usable subject.
    /// </summary>
    Guid? Resolve(ClaimsPrincipal? principal);
}

/// <summary>
/// Reads the account from the token's subject claim.
/// <para>
/// The subject is a provider string, not a GUID, so it is mapped to the stable
/// account id the rest of the system uses. Slice 5 derives that deterministically
/// from the subject; once the API owns an accounts table, this becomes a lookup
/// and nothing else changes.
/// </para>
/// </summary>
public sealed class ClaimsAccountResolver : IAccountResolver
{
    /// <summary>Claims that may carry the provider's stable subject, most specific first.</summary>
    private static readonly string[] SubjectClaims =
    [
        "sub",
        ClaimTypes.NameIdentifier,
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
    ];

    public Guid? Resolve(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = SubjectClaims
            .Select(claim => principal.FindFirst(claim)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // A GUID subject is used as-is; anything else is hashed to one so the
        // mapping is stable across requests without a round trip.
        return Guid.TryParse(subject, out var parsed)
            ? parsed
            : DeterministicAccountId(subject);
    }

    /// <summary>
    /// A stable account id for a non-GUID subject. Deterministic so the same
    /// person always lands on the same account, and one-way so the provider's
    /// subject is not recoverable from data at rest.
    /// </summary>
    internal static Guid DeterministicAccountId(string subject)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("humo:account:" + subject));

        return new Guid(hash.AsSpan(0, 16));
    }
}
