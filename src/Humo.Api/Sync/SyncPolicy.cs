namespace Humo.Api.Sync;

/// <summary>The numbers the sync rules turn on, in one place.</summary>
public static class SyncPolicy
{
    /// <summary>
    /// How far a client timestamp may diverge from server receipt before the
    /// record is flagged.
    /// <para>
    /// Generous on purpose: a cook logged in airplane mode and synced two days
    /// later is a normal event in an offline-first app, not a fault. The
    /// threshold is looking for a clock that is wrong by months, not for a
    /// device that was simply out of signal.
    /// </para>
    /// </summary>
    public static readonly TimeSpan ClockSkewThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// The most records one pull returns. A device returning after a long gap
    /// pages through rather than asking the server to build one enormous
    /// response and the phone to hold it in memory.
    /// </summary>
    public const int MaxPullBatchSize = 500;
}
