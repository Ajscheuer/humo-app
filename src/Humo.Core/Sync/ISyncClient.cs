using Humo.Shared.Sync;

namespace Humo.Core.Sync;

/// <summary>
/// The network half of sync, kept behind an interface so the merge and queue
/// logic is testable without a server, and so an unreachable API is an ordinary
/// return value rather than an exception every caller has to remember.
/// </summary>
public interface ISyncClient
{
    Task<SyncTransportResult<SyncPushResponse>> PushAsync(
        SyncPushRequest request,
        CancellationToken cancellationToken = default);

    Task<SyncTransportResult<SyncPullResponse>> PullAsync(
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>Why a call to the API did not produce an answer.</summary>
public enum SyncTransportFailure
{
    None = 0,

    /// <summary>No usable connection. Normal, not an error: try again later.</summary>
    Unreachable = 1,

    /// <summary>The token was missing, expired or refused. Needs a sign-in, not a retry.</summary>
    Unauthorized = 2,

    /// <summary>The server answered, but with something this client cannot use.</summary>
    Rejected = 3,
}

/// <summary>
/// An answer from the API, or the reason there isn't one.
/// <para>
/// Deliberately not exceptions. Being offline is the expected state of an
/// offline-first app at the pit, and a control flow that treats it as
/// exceptional invites a caller to let it escape into a crash.
/// </para>
/// </summary>
public sealed record SyncTransportResult<T>
    where T : class
{
    private SyncTransportResult()
    {
    }

    public T? Value { get; private init; }

    public SyncTransportFailure Failure { get; private init; }

    public bool Succeeded => Failure == SyncTransportFailure.None && Value is not null;

    public static SyncTransportResult<T> Ok(T value) => new() { Value = value };

    public static SyncTransportResult<T> Failed(SyncTransportFailure failure)
        => new() { Failure = failure };
}
