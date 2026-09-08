using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humo.Shared.Enums;

namespace Humo.Api.Data;

/// <summary>
/// What every synced row carries on the server, on top of what the client sent.
/// <para>
/// The client's own timestamps are stored exactly as sent — never clamped, never
/// rejected. The server adds its own view alongside them, which is what makes
/// the clock-skew policy in <c>architecture.md</c> §5 possible: accept, record,
/// flag.
/// </para>
/// </summary>
public abstract class SyncedRow
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Always taken from the bearer token, never from the request body. A client
    /// that puts someone else's account id in a payload writes into its own.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// The account's monotonic stream position, assigned on write. Pull is
    /// "everything after your cursor", so this is what makes incremental sync
    /// work without comparing timestamps across devices with different clocks.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>Client-set. Stored as sent.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Client-set. The comparand for last-write-wins on mutable records.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Client-set. A tombstone; nothing is ever physically removed by sync.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Server receipt time, recorded alongside the client's own.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Which device last wrote this row, so a pull can skip what the asking
    /// device already has.
    /// <para>
    /// This is what lets the cursor mean one simple thing — how far through the
    /// account's stream you are — instead of being nudged forward on push to
    /// avoid re-sending your own records. That nudge is not safe: it steps over
    /// anything another device wrote before your push, permanently.
    /// </para>
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// True when <see cref="UpdatedAt"/> diverges from <see cref="ReceivedAt"/>
    /// by more than <see cref="SyncPolicy.ClockSkewThreshold"/>.
    /// <para>
    /// Flagged, not altered. The fire model excludes flagged records from learned
    /// intervals, because a wrong clock corrupts cadence far more damagingly than
    /// it corrupts sync ordering.
    /// </para>
    /// </summary>
    public bool ClockSkewFlagged { get; set; }
}

[Table("Equipment")]
public sealed class EquipmentRow : SyncedRow
{
    public string Name { get; set; } = string.Empty;
    public EquipmentType Type { get; set; }
    public double? FireboxVolumeL { get; set; }
    public double? CookChamberVolumeL { get; set; }
    public InsulationLevel Insulation { get; set; }
    public string? Notes { get; set; }
}

[Table("Cooks")]
public sealed class CookRow : SyncedRow
{
    public Guid EquipmentId { get; set; }
    public EquipmentType PitType { get; set; }
    public MeatType MeatType { get; set; }
    public string? MeatTypeOther { get; set; }
    public double WeightKg { get; set; }
    public double? TargetInternalTempC { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public double? AmbientTempC { get; set; }
    public string? Notes { get; set; }
    public int? Rating { get; set; }
    public CookFinishReason? FinishReason { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
}

[Table("TempEntries")]
public sealed class TempEntryRow : SyncedRow
{
    public Guid CookId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public double MeatTempC { get; set; }
    public string? Note { get; set; }
    public TempSource Source { get; set; }
}

[Table("PitTempEntries")]
public sealed class PitTempEntryRow : SyncedRow
{
    public Guid EquipmentId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public double PitTempC { get; set; }
    public double? AmbientTempC { get; set; }
    public string? Note { get; set; }
    public TempSource Source { get; set; }
}

[Table("FuelEvents")]
public sealed class FuelEventRow : SyncedRow
{
    public Guid EquipmentId { get; set; }
    public Guid? CookId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public WoodType WoodType { get; set; }
    public string? WoodTypeOther { get; set; }
    public FuelForm Form { get; set; }
    public SizeClass SizeClass { get; set; }
    public int Count { get; set; }
    public double? WeightKg { get; set; }
    public bool ViaNotification { get; set; }
}

[Table("Events")]
public sealed class EventRow : SyncedRow
{
    public Guid CookId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public EventType Type { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// How far one device has read the account's stream.
/// <para>
/// Per device, not per account: two phones on one account are each at a
/// different point, and a shared cursor would silently starve whichever synced
/// second.
/// </para>
/// <para>
/// The client's own stored cursor is the authoritative one — it is what each
/// pull sends, and it has to be, or a phone that reinstalled could never ask to
/// start over. This row is the server's record of where the device got to:
/// useful for support and for deciding later which devices are worth pushing a
/// notification to, and never consulted when deciding what to return.
/// </para>
/// </summary>
[Table("DeviceCursors")]
public sealed class DeviceCursorRow
{
    public Guid AccountId { get; set; }

    public Guid DeviceId { get; set; }

    public long Cursor { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The account's stream position counter.
/// <para>
/// A row per account rather than a database-wide sequence, so one account's
/// write rate cannot make another account's cursors sparse, and so a purge for
/// account deletion takes its counter with it.
/// </para>
/// </summary>
[Table("AccountSequences")]
public sealed class AccountSequenceRow
{
    [Key]
    public Guid AccountId { get; set; }

    public long LastSequence { get; set; }
}
