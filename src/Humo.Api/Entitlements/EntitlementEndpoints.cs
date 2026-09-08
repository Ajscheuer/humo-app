using Humo.Api.Auth;
using Microsoft.Extensions.Options;

namespace Humo.Api.Entitlements;

public static class EntitlementEndpoints
{
    /// <summary>
    /// The header RevenueCat presents, configured on its dashboard. Its own
    /// convention is to reuse <c>Authorization</c> for a shared secret, which is
    /// not a bearer token and deliberately does not go through the JWT scheme.
    /// </summary>
    internal const string WebhookAuthorizationHeader = "Authorization";

    public static void MapEntitlementEndpoints(this IEndpointRouteBuilder app)
    {
        // What this account may do, and the tier numbers it needs to render the
        // UI. Authorized: an entitlement belongs to an account.
        app.MapGet("/entitlements/me", async (
                IAccountResolver accounts,
                IEntitlementService entitlements,
                HttpContext http,
                CancellationToken cancellationToken) =>
            accounts.Resolve(http.User) is not { } accountId
                ? Results.Unauthorized()
                : Results.Ok(await entitlements.GetAsync(accountId, cancellationToken)))
            .RequireAuthorization();

        // The store telling us what somebody bought. Not authorized in the JWT
        // sense -- there is no user here, only RevenueCat's servers -- so it
        // carries a shared secret instead and is explicitly anonymous.
        app.MapPost("/entitlements/revenuecat", async (
            RevenueCatWebhookBody body,
            IEntitlementService entitlements,
            IOptions<EntitlementOptions> options,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var presented = http.Request.Headers[WebhookAuthorizationHeader].ToString();

            if (!RevenueCatMapping.IsAuthorized(presented, options.Value.WebhookSecret))
            {
                return Results.Unauthorized();
            }

            if (body.ToStoreEvent() is not { } storeEvent)
            {
                // Malformed, or about a subscriber that is not one of ours. A
                // 400 rather than a 500, and rather than a 200: the store should
                // not keep retrying a body that will never parse, but neither
                // should this look like it worked.
                return Results.BadRequest(new { error = "Unrecognised event." });
            }

            var outcome = await entitlements.ApplyAsync(storeEvent, cancellationToken);

            // 200 for a replay and for a stale event too. Both mean "we already
            // know"; anything else makes the store retry forever.
            return Results.Ok(new { outcome = outcome.ToString() });
        })
        .AllowAnonymous();
    }
}
