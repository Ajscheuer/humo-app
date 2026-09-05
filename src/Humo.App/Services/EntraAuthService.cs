using Humo.Core.Identity;
using Humo.Core.Settings;
using Microsoft.Identity.Client;

namespace Humo.App.Services;

/// <summary>
/// Sign-in against Microsoft Entra External ID, using its hosted user flow.
/// <para>
/// MSAL rather than a hand-written OAuth exchange: the whole reason
/// <c>architecture.md</c> Decision 2 chose a managed provider was to avoid owning
/// PKCE, token refresh, and a token cache as security-sensitive code. MSAL also
/// brings the platform's own secure storage for the cache, which is where tokens
/// belong.
/// </para>
/// <para>
/// The hosted flow (Decision 11) renders Entra's own page in a system web view.
/// It ships faster and inherits password reset, lockout and recovery; the cost is
/// that it looks like a web page inside a native app. Swapping to native flows
/// later is a change on this side of <see cref="IAuthService"/> only.
/// </para>
/// </summary>
public sealed class EntraAuthService : IAuthService
{
    /// <summary>Which MSAL account the current sign-in belongs to.</summary>
    internal const string HomeAccountIdKey = "auth.msal.homeAccountId";

    private readonly AuthOptions _options;
    private readonly IAppPreferences _preferences;
    private readonly IPublicClientApplication? _client;

    public EntraAuthService(AuthOptions options, IAppPreferences preferences)
    {
        _options = options;
        _preferences = preferences;

        // Built once, and only when there is a tenant to talk to. Constructing it
        // against blank configuration would throw at startup, long before the
        // user ever taps sign in.
        _client = options.IsConfigured
            ? PublicClientApplicationBuilder
                .Create(options.ClientId)
                .WithAuthority(options.Authority, validateAuthority: false)
                .WithRedirectUri(options.RedirectUri)
                .Build()
            : null;
    }

    public bool IsConfigured => _client is not null;

    public async Task<SignInResult> SignInAsync(
        SignInMethod method,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return SignInResult.Failure(SignInOutcome.NotConfigured);
        }

        try
        {
            // Apple and Google are federated identity providers configured in the
            // tenant, so all three land on the same hosted flow. The method is
            // passed as a domain hint so the user is taken straight to the one
            // they tapped instead of back to a chooser.
            var request = _client
                .AcquireTokenInteractive(_options.Scopes)
                .WithParentActivityOrWindow(ParentWindow());

            var hint = DomainHintFor(method);
            if (hint is not null)
            {
                // In the cache key on purpose: the hint decides which identity
                // provider answers, so an Apple token must not be served back
                // for a Google request.
                request = request.WithExtraQueryParameters(
                    new Dictionary<string, (string Value, bool IncludeInCacheKey)>
                    {
                        ["domain_hint"] = (hint, true),
                    });
            }

            var result = await request.ExecuteAsync(cancellationToken);

            if (ToUser(result, method) is not { } user)
            {
                // A successful token with no subject to key an account on. Rare,
                // but treating it as success would strand whatever the user then
                // logged under an account that cannot be found again.
                return SignInResult.Failure(SignInOutcome.Failed);
            }

            // Remembered so the token lookup later finds *this* user's account
            // rather than whichever one MSAL happens to list first.
            _preferences.SetString(HomeAccountIdKey, result.Account?.HomeAccountId?.Identifier);

            return SignInResult.Success(user);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an outcome here, not a fault: this method promises
            // not to throw for one, and the command above has no catch.
            return SignInResult.Failure(SignInOutcome.Cancelled);
        }
        catch (MsalClientException e) when (e.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            // Backing out of a web view is the single most common outcome here,
            // and it is not a fault.
            return SignInResult.Failure(SignInOutcome.Cancelled);
        }
        catch (MsalServiceException e) when (e.IsRetryable || e.StatusCode == 0)
        {
            return SignInResult.Failure(SignInOutcome.NetworkUnavailable);
        }
        catch (MsalException)
        {
            return SignInResult.Failure(SignInOutcome.Failed);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return;
        }

        foreach (var account in await _client.GetAccountsAsync())
        {
            await _client.RemoveAsync(account);
        }

        _preferences.Remove(HomeAccountIdKey);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return null;
        }

        // By identifier, not "the first one": two people can sign in on one
        // device, and MSAL keeps both in its cache. Handing back whichever it
        // lists first would serve one user's token for the other's data.
        var homeAccountId = _preferences.GetString(HomeAccountIdKey);
        if (string.IsNullOrEmpty(homeAccountId))
        {
            // A guest. No token, which is exactly why they cannot sync.
            return null;
        }

        var accounts = await _client.GetAccountsAsync();
        var account = accounts.FirstOrDefault(
            a => a.HomeAccountId?.Identifier == homeAccountId);

        if (account is null)
        {
            // Signed in on this install, but the cache has been cleared since.
            return null;
        }

        try
        {
            var result = await _client
                .AcquireTokenSilent(_options.Scopes, account)
                .ExecuteAsync(cancellationToken);

            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // The refresh token has expired or been revoked. Returning null lets
            // the caller decide; silently re-prompting from a background sync
            // would throw a login screen at someone mid-cook.
            return null;
        }
    }

    /// <summary>
    /// The tenant's identity-provider hint for a federated button, or null for
    /// the email flow, which is the tenant's own local account.
    /// </summary>
    private static string? DomainHintFor(SignInMethod method) => method switch
    {
        SignInMethod.Apple => "apple.com",
        SignInMethod.Google => "google.com",
        _ => null,
    };

    /// <summary>
    /// The signed-in user, or null when the provider returned no subject to key
    /// an account on. Null rather than throwing: this class promises callers an
    /// outcome, and the sign-in command above has no catch to save them.
    /// </summary>
    private static AuthenticatedUser? ToUser(AuthenticationResult result, SignInMethod method)
    {
        // The provider's stable subject, not the email: an email can change, and
        // re-keying an account off a changed email would strand its data.
        var subject = result.UniqueId ?? result.Account?.HomeAccountId?.Identifier;

        return string.IsNullOrWhiteSpace(subject)
            ? null
            : new AuthenticatedUser
            {
                Subject = subject,
                Method = method,
                Email = result.Account?.Username,
                DisplayName = result.ClaimsPrincipal?.FindFirst("name")?.Value,
            };
    }

    /// <summary>
    /// What the interactive flow parents its browser to. Platform-specific:
    /// <c>Platform.CurrentActivity</c> exists only on Android, and referencing it
    /// unguarded breaks the iOS build.
    /// </summary>
    private static object? ParentWindow()
    {
#if ANDROID
        return Platform.CurrentActivity;
#elif IOS || MACCATALYST
        return Platform.GetCurrentUIViewController();
#else
        return null;
#endif
    }
}
