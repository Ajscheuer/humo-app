using Humo.Api.Data;
using Humo.Shared.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Humo.Api.Entitlements;

/// <summary>
/// What an account is entitled to, and applying what the store says.
/// <para>
/// The single answer to "is this account Pro?" for the whole API. Every
/// Pro-gated path asks this rather than trusting anything in a request.
/// </para>
/// </summary>
public interface IEntitlementService
{
    Task<EntitlementState> GetAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what the store reported. Idempotent, and never applies an event
    /// older than the one already recorded.
    /// </summary>
    Task<EntitlementUpdate> ApplyAsync(StoreEntitlementEvent storeEvent, CancellationToken cancellationToken = default);
}

/// <summary>What the store told us, normalised out of its own payload shape.</summary>
public sealed record StoreEntitlementEvent
{
    public required Guid AccountId { get; init; }

    /// <summary>The store's event id, for recognising a replay.</summary>
    public required string EventId { get; init; }

    /// <summary>When the store says it happened, not when it reached us.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Entitlement identifiers active for this subscriber. Empty means none —
    /// a cancellation or an expiry, which is a downgrade rather than a no-op.
    /// </summary>
    public IReadOnlyList<string> ActiveEntitlementIds { get; init; } = [];

    /// <summary>End of the paid period, or null for lifetime.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public string? StoreSubscriberId { get; init; }

    public string? ProductId { get; init; }
}

/// <summary>Whether a store event changed anything.</summary>
public enum EntitlementUpdate
{
    Applied = 0,

    /// <summary>Already recorded. A store retrying a delivery is normal.</summary>
    Replay = 1,

    /// <summary>An older event arriving after a newer one. Ignored, not applied.</summary>
    Stale = 2,
}

internal sealed class EntitlementService : IEntitlementService
{
    private readonly HumoDbContext _db;
    private readonly TimeProvider _time;
    private readonly EntitlementOptions _options;

    public EntitlementService(HumoDbContext db, TimeProvider time, IOptions<EntitlementOptions> options)
    {
        _db = db;
        _time = time;
        _options = options.Value;
    }

    public async Task<EntitlementState> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.Entitlements
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false);

        return new EntitlementState
        {
            Level = LevelOf(row),
            ExpiresAt = row?.ExpiresAt,
            Policy = CurrentPolicy(),
        };
    }

    public async Task<EntitlementUpdate> ApplyAsync(
        StoreEntitlementEvent storeEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storeEvent);

        var row = await _db.Entitlements
            .FirstOrDefaultAsync(e => e.AccountId == storeEvent.AccountId, cancellationToken)
            .ConfigureAwait(false);

        if (row is not null)
        {
            // Stores retry deliveries. Applying the same event twice is harmless
            // for a level but not for the audit trail, and saying so lets the
            // store stop retrying.
            if (row.LastEventId == storeEvent.EventId)
            {
                return EntitlementUpdate.Replay;
            }

            // Delivery order is not guaranteed. A cancellation that overtakes the
            // renewal it precedes would otherwise cut off a paying subscriber.
            if (row.LastEventAt is { } recorded && storeEvent.OccurredAt < recorded)
            {
                return EntitlementUpdate.Stale;
            }
        }

        if (row is null)
        {
            row = new EntitlementRow { AccountId = storeEvent.AccountId };
            _db.Entitlements.Add(row);
        }

        // An event with no active entitlements is a downgrade, not an absence of
        // news: it is how a cancellation or a refund arrives.
        row.Level = storeEvent.ActiveEntitlementIds.Any(IsPro)
            ? EntitlementLevel.Pro
            : EntitlementLevel.Free;

        row.ExpiresAt = storeEvent.ExpiresAt;
        row.StoreSubscriberId = storeEvent.StoreSubscriberId;
        row.ProductId = storeEvent.ProductId;
        row.LastEventId = storeEvent.EventId;
        row.LastEventAt = storeEvent.OccurredAt;
        row.UpdatedAt = _time.GetUtcNow();

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return EntitlementUpdate.Applied;
    }

    private bool IsPro(string entitlementId)
        => _options.ProEntitlementIds.Any(
            id => string.Equals(id, entitlementId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pro only while the paid period still runs.
    /// <para>
    /// Checked on read rather than swept on a schedule, so a subscription that
    /// lapses while the server is idle stops being Pro at the moment it lapses
    /// and not whenever a job next happens to run.
    /// </para>
    /// </summary>
    private EntitlementLevel LevelOf(EntitlementRow? row)
    {
        if (row is null || row.Level != EntitlementLevel.Pro)
        {
            return EntitlementLevel.Free;
        }

        // Null expiry is a lifetime purchase, not an immediate expiry.
        return row.ExpiresAt is { } expires && expires <= _time.GetUtcNow()
            ? EntitlementLevel.Free
            : EntitlementLevel.Pro;
    }

    private EntitlementPolicy CurrentPolicy() => new()
    {
        // Zero is a harsh but coherent setting — no free history at all. A
        // negative one is a typo, and passing it on would have the client
        // comparing against nonsense.
        FreeCookHistoryLimit = Math.Max(0, _options.FreeCookHistoryLimit),
    };
}
