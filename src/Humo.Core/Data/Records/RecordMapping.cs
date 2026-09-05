using Humo.Shared.Entities;

namespace Humo.Core.Data.Records;

/// <summary>
/// Translates between the shared entities and the persistence records.
/// <para>
/// Mechanical by design. The only asymmetry worth noticing is <c>SyncedAt</c>:
/// it exists on the record and not on the entity, so writing a record must not
/// clobber it with a value the entity never carried. Every write here goes
/// through a caller that supplies it explicitly.
/// </para>
/// </summary>
internal static class RecordMapping
{
    public static Equipment ToEntity(this EquipmentRecord r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        DeletedAt = r.DeletedAt,
        Name = r.Name,
        Type = r.Type,
        FireboxVolumeL = r.FireboxVolumeL,
        CookChamberVolumeL = r.CookChamberVolumeL,
        Insulation = r.Insulation,
        Notes = r.Notes,
    };

    public static EquipmentRecord ToRecord(this Equipment e, DateTimeOffset? syncedAt = null) => new()
    {
        Id = e.Id,
        AccountId = e.AccountId,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        DeletedAt = e.DeletedAt,
        SyncedAt = syncedAt,
        Name = e.Name,
        Type = e.Type,
        FireboxVolumeL = e.FireboxVolumeL,
        CookChamberVolumeL = e.CookChamberVolumeL,
        Insulation = e.Insulation,
        Notes = e.Notes,
    };

    public static Cook ToEntity(this CookRecord r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        DeletedAt = r.DeletedAt,
        EquipmentId = r.EquipmentId,
        PitType = r.PitType,
        MeatType = r.MeatType,
        MeatTypeOther = r.MeatTypeOther,
        WeightKg = r.WeightKg,
        TargetInternalTempC = r.TargetInternalTempC,
        StartedAt = r.StartedAt,
        FinishedAt = r.FinishedAt,
        AmbientTempC = r.AmbientTempC,
        Notes = r.Notes,
        Rating = r.Rating,
        FinishReason = r.FinishReason,
        LastActivityAt = r.LastActivityAt,
    };

    public static CookRecord ToRecord(this Cook c, DateTimeOffset? syncedAt = null) => new()
    {
        Id = c.Id,
        AccountId = c.AccountId,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
        DeletedAt = c.DeletedAt,
        SyncedAt = syncedAt,
        EquipmentId = c.EquipmentId,
        PitType = c.PitType,
        MeatType = c.MeatType,
        MeatTypeOther = c.MeatTypeOther,
        WeightKg = c.WeightKg,
        TargetInternalTempC = c.TargetInternalTempC,
        StartedAt = c.StartedAt,
        FinishedAt = c.FinishedAt,
        AmbientTempC = c.AmbientTempC,
        Notes = c.Notes,
        Rating = c.Rating,
        FinishReason = c.FinishReason,
        LastActivityAt = c.LastActivityAt,
    };

    public static TempEntry ToEntity(this TempEntryRecord r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        DeletedAt = r.DeletedAt,
        CookId = r.CookId,
        RecordedAt = r.RecordedAt,
        MeatTempC = r.MeatTempC,
        Note = r.Note,
        Source = r.Source,
    };

    public static TempEntryRecord ToRecord(this TempEntry t, DateTimeOffset? syncedAt = null) => new()
    {
        Id = t.Id,
        AccountId = t.AccountId,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        DeletedAt = t.DeletedAt,
        SyncedAt = syncedAt,
        CookId = t.CookId,
        RecordedAt = t.RecordedAt,
        MeatTempC = t.MeatTempC,
        Note = t.Note,
        Source = t.Source,
    };

    public static PitTempEntry ToEntity(this PitTempEntryRecord r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        DeletedAt = r.DeletedAt,
        EquipmentId = r.EquipmentId,
        RecordedAt = r.RecordedAt,
        PitTempC = r.PitTempC,
        AmbientTempC = r.AmbientTempC,
        Note = r.Note,
        Source = r.Source,
    };

    public static PitTempEntryRecord ToRecord(this PitTempEntry p, DateTimeOffset? syncedAt = null) => new()
    {
        Id = p.Id,
        AccountId = p.AccountId,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        DeletedAt = p.DeletedAt,
        SyncedAt = syncedAt,
        EquipmentId = p.EquipmentId,
        RecordedAt = p.RecordedAt,
        PitTempC = p.PitTempC,
        AmbientTempC = p.AmbientTempC,
        Note = p.Note,
        Source = p.Source,
    };
}
