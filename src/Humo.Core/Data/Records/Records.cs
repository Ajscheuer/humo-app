using Humo.Shared.Enums;
using SQLite;

namespace Humo.Core.Data.Records;

// Persistence models, deliberately separate from the entities in Humo.Shared.
//
// Humo.Shared is the wire contract and references nothing -- a rule the
// conventions tests enforce -- so it cannot carry sqlite-net attributes. Keeping
// storage shape separate from contract shape also means a column can be renamed
// or indexed without touching what the API and app agree on.
//
// syncedAt lives here and only here: it is local bookkeeping and is never sent.

[Table("equipment")]
internal sealed class EquipmentRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    public string Name { get; set; } = string.Empty;
    public EquipmentType Type { get; set; }
    public double? FireboxVolumeL { get; set; }
    public double? CookChamberVolumeL { get; set; }
    public InsulationLevel Insulation { get; set; }
    public string? Notes { get; set; }
}

[Table("cooks")]
internal sealed class CookRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    [Indexed] public Guid EquipmentId { get; set; }
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

[Table("temp_entries")]
internal sealed class TempEntryRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    // Indexed on (CookId, RecordedAt) from the start. Manual entries arrive every
    // 20-60 minutes, but probe data (fire model Level 3) arrives every 30 seconds
    // -- roughly 1,700 rows for a 14-hour cook. Adding the index later would be a
    // migration on a table that has grown; adding it now costs nothing.
    [Indexed(Name = "ix_temp_cook_time", Order = 1)] public Guid CookId { get; set; }
    [Indexed(Name = "ix_temp_cook_time", Order = 2)] public DateTimeOffset RecordedAt { get; set; }

    public double MeatTempC { get; set; }
    public string? Note { get; set; }
    public TempSource Source { get; set; }
}

[Table("pit_temp_entries")]
internal sealed class PitTempEntryRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    // Scoped to the rig, not the cook: one fire, one temperature.
    [Indexed(Name = "ix_pit_equipment_time", Order = 1)] public Guid EquipmentId { get; set; }
    [Indexed(Name = "ix_pit_equipment_time", Order = 2)] public DateTimeOffset RecordedAt { get; set; }

    public double PitTempC { get; set; }
    public double? AmbientTempC { get; set; }
    public string? Note { get; set; }
    public TempSource Source { get; set; }
}

[Table("fuel_events")]
internal sealed class FuelEventRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    // Scoped to the rig for the same reason pit temperatures are: one fire. The
    // index is (EquipmentId, RecordedAt) because every read is "the fuel history
    // for this rig, in order" -- pre-filling the sheet, and the fire model's
    // cadence learning.
    [Indexed(Name = "ix_fuel_equipment_time", Order = 1)] public Guid EquipmentId { get; set; }
    [Indexed(Name = "ix_fuel_equipment_time", Order = 2)] public DateTimeOffset RecordedAt { get; set; }

    public Guid? CookId { get; set; }
    public WoodType WoodType { get; set; }
    public string? WoodTypeOther { get; set; }
    public FuelForm Form { get; set; }
    public SizeClass SizeClass { get; set; }
    public int Count { get; set; }
    public double? WeightKg { get; set; }
    public bool ViaNotification { get; set; }
}

[Table("events")]
internal sealed class EventRecord
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    // Per cook, unlike fuel: a milestone applies to one piece of meat.
    [Indexed(Name = "ix_event_cook_time", Order = 1)] public Guid CookId { get; set; }
    [Indexed(Name = "ix_event_cook_time", Order = 2)] public DateTimeOffset RecordedAt { get; set; }

    public EventType Type { get; set; }
    public string? Note { get; set; }
}
