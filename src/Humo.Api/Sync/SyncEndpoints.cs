using Humo.Api.Auth;
using Humo.Shared.Sync;

namespace Humo.Api.Sync;

/// <summary>
/// The sync endpoints. Registered from <c>Program.cs</c>, which stays a wiring
/// file and does not accumulate endpoint bodies.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").RequireAuthorization();

        group.MapPost("/push", async (
            SyncPushRequest request,
            IAccountResolver accounts,
            ISyncService sync,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (accounts.Resolve(http.User) is not { } accountId)
            {
                return Results.Unauthorized();
            }

            if (request.DeviceId == Guid.Empty)
            {
                // Cursors are per device. Without an id there is nowhere to record
                // how far this caller has read, and every pull would replay
                // everything.
                return Results.BadRequest(new { error = "deviceId is required." });
            }

            var response = await sync.PushAsync(accountId, request, cancellationToken);

            // 200 even with rejections: the batch was processed and the body says
            // exactly which records need re-sending with their parents. A 4xx
            // would tell the client to stop, when what it must do is retry a
            // subset.
            return Results.Ok(response);
        });

        group.MapGet("/pull", async (
            Guid deviceId,
            long cursor,
            IAccountResolver accounts,
            ISyncService sync,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (accounts.Resolve(http.User) is not { } accountId)
            {
                return Results.Unauthorized();
            }

            if (deviceId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "deviceId is required." });
            }

            if (cursor < 0)
            {
                return Results.BadRequest(new { error = "cursor cannot be negative." });
            }

            return Results.Ok(await sync.PullAsync(accountId, deviceId, cursor, cancellationToken));
        });
    }
}
