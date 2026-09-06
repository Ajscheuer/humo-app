using Humo.Core.Data;
using Humo.Core.Identity;
using Humo.Core.Time;

namespace Humo.Core.Sync;

/// <summary>How a sync attempt ended.</summary>
public enum SyncOutcome
{
    /// <summary>Everything queued went up and everything waiting came down.</summary>
    Completed = 0,

    /// <summary>Nothing to send and nothing to collect.</summary>
    NothingToDo = 1,

    /// <summary>No connection. Expected at a pit; try again later.</summary>
    Offline = 2,

    /// <summary>A guest, or a token the server refused. Needs a sign-in.</summary>
    NotSignedIn = 3,

    /// <summary>
    /// The server took some of the batch and asked for the rest again. The
    /// rejected records stay queued.
    /// </summary>
    PartiallyApplied = 4,

    /// <summary>Already syncing. The caller's request was dropped, not queued.</summary>
    AlreadyRunning = 5,
}

public sealed record SyncResult
{
    public required SyncOutcome Outcome { get; init; }

    public int Pushed { get; init; }

    public int Pulled { get; init; }

    /// <summary>Records the server asked for again. They are still queued.</summary>
    public int Deferred { get; init; }

    /// <summary>True when there is more waiting on the server than one pull returned.</summary>
    public bool HasMore { get; init; }

    public static SyncResult Of(SyncOutcome outcome) => new() { Outcome = outcome };
}

/// <summary>
/// One round of sync: push what is queued, pull what is waiting.
/// <para>
/// Never throws for an unreachable server and never blocks a user action —
/// SQLite is the source of truth during a cook, and this is bookkeeping that
/// happens around it.
/// </para>
/// </summary>
public interface ISyncService
{
    Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

internal sealed class SyncService : ISyncService
{
    private readonly ISyncQueue _queue;
    private readonly ISyncClient _client;
    private readonly ISyncState _state;
    private readonly IAccountContext _account;
    private readonly IClock _clock;

    // One round at a time. Two overlapping rounds would push the same records
    // twice -- harmless, the server is idempotent -- but would also race on the
    // cursor and on SyncedAt, which is not.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(
        ISyncQueue queue,
        ISyncClient client,
        ISyncState state,
        IAccountContext account,
        IClock clock)
    {
        _queue = queue;
        _client = client;
        _state = state;
        _account = account;
        _clock = clock;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (_account.IsAnonymous)
        {
            // A guest has no account on the server. Not a failure: it is the
            // "continue without an account" path working as designed.
            return SyncResult.Of(SyncOutcome.NotSignedIn);
        }

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return SyncResult.Of(SyncOutcome.AlreadyRunning);
        }

        try
        {
            return await RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A ceiling on the rounds one sync will run, in each direction. Both loops
    /// terminate on their own; this only bounds the damage if a server ever
    /// answered in a way that did not make progress, so a background sync cannot
    /// spin on a phone in someone's pocket.
    /// </summary>
    private const int MaxRounds = 50;

    private async Task<SyncResult> RunAsync(CancellationToken cancellationToken)
    {
        var accountId = _account.CurrentAccountId;
        var deviceId = _state.DeviceId;

        var pushed = 0;
        var deferred = 0;

        // Batches are capped, so a device back after a season needs several
        // rounds. Doing them here rather than waiting for the next launch is the
        // difference between catching up today and catching up in a month.
        for (var round = 0; round < MaxRounds; round++)
        {
            var collectedAt = _clock.UtcNow;
            var batch = await _queue
                .CollectAsync(deviceId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            var push = await _client.PushAsync(batch, cancellationToken).ConfigureAwait(false);

            if (!push.Succeeded)
            {
                // Nothing is acknowledged, so this batch is still queued. Pulling
                // now would be safe but pointless: the same connection just
                // failed.
                return new SyncResult
                {
                    Outcome = Translate(push.Failure),
                    Pushed = pushed,
                    Deferred = deferred,
                };
            }

            var rejectedIds = push.Value!.Rejected.Select(r => r.RecordId).ToList();

            await _queue
                .AcknowledgeAsync(batch, rejectedIds, collectedAt, cancellationToken)
                .ConfigureAwait(false);

            pushed += batch.Count - rejectedIds.Count;
            deferred = rejectedIds.Count;

            // Nothing was acknowledged, so the next round would collect exactly
            // this batch again. Stopping is what keeps a wholly rejected batch
            // from spinning; the records stay queued for the next sync.
            if (rejectedIds.Count == batch.Count)
            {
                break;
            }

            // The cursor is not touched by a push. It moved records onto the
            // server; it did not move this device through the stream, and the
            // pull below still has to collect whatever another device wrote
            // while this one was offline.
        }

        var pulled = 0;
        var hasMore = false;

        for (var round = 0; round < MaxRounds; round++)
        {
            var cursor = _state.GetCursor(accountId);
            var pull = await _client.PullAsync(deviceId, cursor, cancellationToken).ConfigureAwait(false);

            if (!pull.Succeeded)
            {
                // The push still counts: those records are on the server and
                // acknowledged locally.
                return new SyncResult
                {
                    Outcome = Translate(pull.Failure),
                    Pushed = pushed,
                    Pulled = pulled,
                    Deferred = deferred,
                };
            }

            pulled += await _queue
                .ApplyAsync(pull.Value!, _clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            _state.SetCursor(accountId, pull.Value!.Cursor);
            hasMore = pull.Value.HasMore;

            // A page that reported more but did not move the cursor would loop
            // forever otherwise.
            if (!hasMore || _state.GetCursor(accountId) == cursor)
            {
                break;
            }
        }

        var outcome = deferred > 0
            ? SyncOutcome.PartiallyApplied
            : pushed == 0 && pulled == 0
                ? SyncOutcome.NothingToDo
                : SyncOutcome.Completed;

        return new SyncResult
        {
            Outcome = outcome,
            Pushed = pushed,
            Pulled = pulled,
            Deferred = deferred,
            HasMore = hasMore,
        };
    }

    private static SyncOutcome Translate(SyncTransportFailure failure) => failure switch
    {
        SyncTransportFailure.Unauthorized => SyncOutcome.NotSignedIn,

        // A malformed answer is treated as offline rather than as a fault the
        // user can act on. There is nothing for them to do about it, and the
        // queue survives either way.
        _ => SyncOutcome.Offline,
    };
}
