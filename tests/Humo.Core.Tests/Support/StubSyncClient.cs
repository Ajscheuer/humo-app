using Humo.Core.Sync;
using Humo.Shared.Sync;

namespace Humo.Core.Tests.Support;

/// <summary>
/// A server that does exactly what the test tells it to. Used for the
/// orchestration tests, where what matters is how <c>SyncService</c> reacts to
/// each answer — the merge rules themselves are the API's tests.
/// </summary>
internal sealed class StubSyncClient : ISyncClient
{
    public List<SyncPushRequest> Pushes { get; } = [];

    public List<(Guid Device, long Cursor)> Pulls { get; } = [];

    public SyncTransportFailure? PushFailure { get; set; }

    public SyncTransportFailure? PullFailure { get; set; }

    /// <summary>Ids the server refuses. Anything else in a batch is accepted.</summary>
    public List<Guid> Reject { get; } = [];

    public SyncPullResponse PullResponse { get; set; } = new() { Cursor = 0, HasMore = false };

    /// <summary>
    /// Pages handed out one per pull, for the catching-up case. The last one is
    /// repeated once the queue runs dry, the way a caught-up server would keep
    /// answering "nothing new".
    /// </summary>
    public Queue<SyncPullResponse> PullPages { get; } = new();

    public Task<SyncTransportResult<SyncPushResponse>> PushAsync(
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        Pushes.Add(request);

        if (PushFailure is { } failure)
        {
            return Task.FromResult(SyncTransportResult<SyncPushResponse>.Failed(failure));
        }

        var rejected = request.InApplyOrder()
            .Where(e => Reject.Contains(e.Id))
            .Select(e => new SyncRejection
            {
                RecordId = e.Id,
                RecordType = e.GetType().Name,
                Reason = SyncRejectionReason.ParentMissing,
            })
            .ToList();

        return Task.FromResult(SyncTransportResult<SyncPushResponse>.Ok(new SyncPushResponse
        {
            Accepted = request.Count - rejected.Count,
            Rejected = rejected,
        }));
    }

    public Task<SyncTransportResult<SyncPullResponse>> PullAsync(
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken = default)
    {
        Pulls.Add((deviceId, cursor));

        if (PullFailure is { } failure)
        {
            return Task.FromResult(SyncTransportResult<SyncPullResponse>.Failed(failure));
        }

        if (PullPages.Count > 0)
        {
            PullResponse = PullPages.Dequeue();
        }

        return Task.FromResult(SyncTransportResult<SyncPullResponse>.Ok(PullResponse));
    }
}
