using Humo.Shared.Entities;

namespace Humo.Shared.Sync;

/// <summary>
/// A batch of records going up to the server.
/// <para>
/// The collections are declared parents-before-children, and the server applies
/// them in that order. Making the ordering part of the <em>shape</em> rather than
/// a rule about a flat list means a client cannot get it wrong by accident: the
/// normal case simply cannot produce an orphan.
/// </para>
/// </summary>
public sealed record SyncPushRequest
{
    /// <summary>
    /// Which device is pushing. Cursors are per device, because two phones on one
    /// account are each at a different point in the stream.
    /// </summary>
    public required Guid DeviceId { get; init; }

    public IReadOnlyList<Equipment> Equipment { get; init; } = [];

    public IReadOnlyList<Cook> Cooks { get; init; } = [];

    public IReadOnlyList<TempEntry> TempEntries { get; init; } = [];

    public IReadOnlyList<PitTempEntry> PitTempEntries { get; init; } = [];

    public IReadOnlyList<FuelEvent> FuelEvents { get; init; } = [];

    public IReadOnlyList<Event> Events { get; init; } = [];

    /// <summary>Every record in the batch, in the order the server must apply them.</summary>
    public IEnumerable<Entity> InApplyOrder() =>
        Equipment.Cast<Entity>()
            .Concat(Cooks)
            .Concat(TempEntries)
            .Concat(PitTempEntries)
            .Concat(FuelEvents)
            .Concat(Events);

    public int Count => InApplyOrder().Count();
}

/// <summary>Why one record in a batch was not applied.</summary>
public enum SyncRejectionReason
{
    /// <summary>
    /// Its parent is neither in this batch nor already on the server. Retryable:
    /// the client re-sends it with its parent. Never held server-side, never
    /// silently dropped.
    /// </summary>
    ParentMissing = 0,

    /// <summary>The record failed validation and will never be accepted as sent.</summary>
    Invalid = 1,
}

/// <summary>One record the server did not apply, and why.</summary>
public sealed record SyncRejection
{
    public required Guid RecordId { get; init; }

    /// <summary>The entity type name, so the client knows which collection to resend from.</summary>
    public required string RecordType { get; init; }

    public required SyncRejectionReason Reason { get; init; }

    /// <summary>True when re-sending with the parent could succeed.</summary>
    public bool IsRetryable => Reason == SyncRejectionReason.ParentMissing;
}

public sealed record SyncPushResponse
{
    /// <summary>
    /// How many records changed server state. A replayed batch reports zero and
    /// no rejections, which is success, not failure: the records are already
    /// there. Use <see cref="HasRejections"/>, never this count, to decide
    /// whether anything needs re-sending.
    /// </summary>
    public required int Accepted { get; init; }

    public IReadOnlyList<SyncRejection> Rejected { get; init; } = [];

    // No cursor here, deliberately. A push does not move the device through the
    // stream: what it wrote is skipped on pull because the server records which
    // device wrote each row. Returning a cursor from a push would invite the
    // client to jump to it and step over everything another device wrote before
    // the push — permanently, and with nothing to notice.

    public bool HasRejections => Rejected.Count > 0;
}

/// <summary>
/// What the server has that this device has not seen.
/// <para>
/// Collections come back parents-before-children for the same reason they go up
/// that way: the client applies them in order and never has to hold an orphan.
/// </para>
/// </summary>
public sealed record SyncPullResponse
{
    public IReadOnlyList<Equipment> Equipment { get; init; } = [];

    public IReadOnlyList<Cook> Cooks { get; init; } = [];

    public IReadOnlyList<TempEntry> TempEntries { get; init; } = [];

    public IReadOnlyList<PitTempEntry> PitTempEntries { get; init; } = [];

    public IReadOnlyList<FuelEvent> FuelEvents { get; init; } = [];

    public IReadOnlyList<Event> Events { get; init; } = [];

    /// <summary>The cursor to send on the next pull.</summary>
    public required long Cursor { get; init; }

    /// <summary>
    /// True when the server truncated this page. The client pulls again
    /// immediately rather than waiting for the next sync — a device returning
    /// after a month should not need a month of app launches to catch up.
    /// </summary>
    public required bool HasMore { get; init; }

    public int Count =>
        Equipment.Count + Cooks.Count + TempEntries.Count
        + PitTempEntries.Count + FuelEvents.Count + Events.Count;
}
