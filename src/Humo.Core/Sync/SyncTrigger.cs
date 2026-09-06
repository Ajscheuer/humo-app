namespace Humo.Core.Sync;

/// <summary>
/// Asks for a sync without waiting for one.
/// <para>
/// The app calls this from lifecycle events — launch, resume — where there is
/// nothing to await into and nowhere for an exception to go. Keeping the
/// fire-and-forget in one tested place is what stops it being written inline in
/// a code-behind, where a failed sync would take the app down with it.
/// </para>
/// </summary>
public interface ISyncTrigger
{
    /// <summary>
    /// Starts a sync if one is not already running, and returns immediately.
    /// Never throws.
    /// </summary>
    void RequestSync();

    /// <summary>The last round's result, for a test or a future status line.</summary>
    SyncResult? LastResult { get; }
}

internal sealed class SyncTrigger : ISyncTrigger
{
    private readonly ISyncService _sync;
    private readonly ISyncFailureLog _log;

    public SyncTrigger(ISyncService sync, ISyncFailureLog log)
    {
        _sync = sync;
        _log = log;
    }

    public SyncResult? LastResult { get; private set; }

    public void RequestSync() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            LastResult = await _sync.SyncAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Nothing above this can handle it: an unobserved task exception on
            // a background thread is a crash on both platforms, and a cook in
            // progress is worth more than a sync round.
            _log.Record(e);
        }
    }
}

/// <summary>Where a swallowed sync failure goes, so it is not simply invisible.</summary>
public interface ISyncFailureLog
{
    void Record(Exception exception);

    /// <summary>The most recent failure, or null. Read by tests today.</summary>
    Exception? Last { get; }
}

internal sealed class SyncFailureLog : ISyncFailureLog
{
    public Exception? Last { get; private set; }

    public void Record(Exception exception) => Last = exception;
}
