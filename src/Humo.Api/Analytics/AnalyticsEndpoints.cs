using Humo.Api.Auth;
using Humo.Api.Entitlements;

namespace Humo.Api.Analytics;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // The first Pro-gated endpoint. RequirePro checks the entitlement row
        // server-side, so a client that edits its cached tier gets a 402 here
        // rather than the numbers.
        app.MapGet("/analytics/cooks/{cookId:guid}", async (
                Guid cookId,
                IAccountResolver accounts,
                IAnalyticsService analytics,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (accounts.Resolve(http.User) is not { } accountId)
                {
                    return Results.Unauthorized();
                }

                return await analytics.GetAsync(accountId, cookId, cancellationToken) is { } computed
                    ? Results.Ok(computed)

                    // Not this account's cook, or one the server has never
                    // computed. The same answer either way, so the endpoint does
                    // not tell a caller which cook ids exist on other accounts.
                    : Results.NotFound();
            })
            .RequirePro();
    }
}
