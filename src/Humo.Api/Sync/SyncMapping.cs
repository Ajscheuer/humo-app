using Humo.Api.Data;
using Humo.Shared.Entities;

namespace Humo.Api.Sync;

/// <summary>
/// Between the wire entities and the server's rows.
/// <para>
/// Mechanical, with one rule that is not: <c>AccountId</c> is never copied from
/// the incoming entity. It comes from the token, and these methods take it as a
/// parameter so there is no overload that could accidentally trust the payload.
/// </para>
/// </summary>
internal static class SyncMapping
{
    public static void ApplyEnvelope(
        this SyncedRow row,
        Entity entity,
        Guid accountId,
        Guid deviceId,
        DateTimeOffset receivedAt)
    {
        row.Id = entity.Id;

        // From the token. A client that puts another account's id in the body
        // writes into its own, which is the whole point.
        row.AccountId = accountId;

        // Who wrote this version, so a pull can skip handing it straight back.
        row.DeviceId = deviceId;

        row.CreatedAt = entity.CreatedAt;
        row.UpdatedAt = entity.UpdatedAt;
        row.DeletedAt = entity.DeletedAt;
        row.ReceivedAt = receivedAt;

        // Accept, record, flag. Never clamp: clamping would collapse a cook
        // logged offline over two days into a single instant and wreck every
        // interval in it.
        var divergence = entity.UpdatedAt > receivedAt
            ? entity.UpdatedAt - receivedAt
            : receivedAt - entity.UpdatedAt;
        row.ClockSkewFlagged = divergence > SyncPolicy.ClockSkewThreshold;
    }

    public static void CopyFrom(this EquipmentRow row, Equipment e)
    {
        row.Name = e.Name;
        row.Type = e.Type;
        row.FireboxVolumeL = e.FireboxVolumeL;
        row.CookChamberVolumeL = e.CookChamberVolumeL;
        row.Insulation = e.Insulation;
        row.Notes = e.Notes;
    }

    public static void CopyFrom(this CookRow row, Cook c)
    {
        row.EquipmentId = c.EquipmentId;
        row.PitType = c.PitType;
        row.MeatType = c.MeatType;
        row.MeatTypeOther = c.MeatTypeOther;
        row.WeightKg = c.WeightKg;
        row.TargetInternalTempC = c.TargetInternalTempC;
        row.StartedAt = c.StartedAt;
        row.FinishedAt = c.FinishedAt;
        row.AmbientTempC = c.AmbientTempC;
        row.Notes = c.Notes;
        row.Rating = c.Rating;
        row.FinishReason = c.FinishReason;
        row.LastActivityAt = c.LastActivityAt;
    }

    public static void CopyFrom(this TempEntryRow row, TempEntry t)
    {
        row.CookId = t.CookId;
        row.RecordedAt = t.RecordedAt;
        row.MeatTempC = t.MeatTempC;
        row.Note = t.Note;
        row.Source = t.Source;
    }

    public static void CopyFrom(this PitTempEntryRow row, PitTempEntry p)
    {
        row.EquipmentId = p.EquipmentId;
        row.RecordedAt = p.RecordedAt;
        row.PitTempC = p.PitTempC;
        row.AmbientTempC = p.AmbientTempC;
        row.Note = p.Note;
        row.Source = p.Source;
    }

    public static void CopyFrom(this FuelEventRow row, FuelEvent f)
    {
        row.EquipmentId = f.EquipmentId;
        row.CookId = f.CookId;
        row.RecordedAt = f.RecordedAt;
        row.WoodType = f.WoodType;
        row.WoodTypeOther = f.WoodTypeOther;
        row.Form = f.Form;
        row.SizeClass = f.SizeClass;
        row.Count = f.Count;
        row.WeightKg = f.WeightKg;
        row.ViaNotification = f.ViaNotification;
    }

    public static void CopyFrom(this EventRow row, Event e)
    {
        row.CookId = e.CookId;
        row.RecordedAt = e.RecordedAt;
        row.Type = e.Type;
        row.Note = e.Note;
    }

    private static void FillEntity(Entity entity, SyncedRow row)
    {
        entity.Id = row.Id;
        entity.AccountId = row.AccountId;
        entity.CreatedAt = row.CreatedAt;
        entity.UpdatedAt = row.UpdatedAt;
        entity.DeletedAt = row.DeletedAt;
    }

    public static Equipment ToEntity(this EquipmentRow r)
    {
        var e = new Equipment
        {
            Name = r.Name,
            Type = r.Type,
            FireboxVolumeL = r.FireboxVolumeL,
            CookChamberVolumeL = r.CookChamberVolumeL,
            Insulation = r.Insulation,
            Notes = r.Notes,
        };
        FillEntity(e, r);
        return e;
    }

    public static Cook ToEntity(this CookRow r)
    {
        var c = new Cook
        {
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
        FillEntity(c, r);
        return c;
    }

    public static TempEntry ToEntity(this TempEntryRow r)
    {
        var t = new TempEntry
        {
            CookId = r.CookId,
            RecordedAt = r.RecordedAt,
            MeatTempC = r.MeatTempC,
            Note = r.Note,
            Source = r.Source,
        };
        FillEntity(t, r);
        return t;
    }

    public static PitTempEntry ToEntity(this PitTempEntryRow r)
    {
        var p = new PitTempEntry
        {
            EquipmentId = r.EquipmentId,
            RecordedAt = r.RecordedAt,
            PitTempC = r.PitTempC,
            AmbientTempC = r.AmbientTempC,
            Note = r.Note,
            Source = r.Source,
        };
        FillEntity(p, r);
        return p;
    }

    public static FuelEvent ToEntity(this FuelEventRow r)
    {
        var f = new FuelEvent
        {
            EquipmentId = r.EquipmentId,
            CookId = r.CookId,
            RecordedAt = r.RecordedAt,
            WoodType = r.WoodType,
            WoodTypeOther = r.WoodTypeOther,
            Form = r.Form,
            SizeClass = r.SizeClass,
            Count = r.Count,
            WeightKg = r.WeightKg,
            ViaNotification = r.ViaNotification,
        };
        FillEntity(f, r);
        return f;
    }

    public static Event ToEntity(this EventRow r)
    {
        var e = new Event
        {
            CookId = r.CookId,
            RecordedAt = r.RecordedAt,
            Type = r.Type,
            Note = r.Note,
        };
        FillEntity(e, r);
        return e;
    }
}
