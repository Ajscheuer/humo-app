using Humo.Core.Data.Records;
using Humo.Shared.Entities;

namespace Humo.Core.Data;

/// <summary>Equipment reads and writes. Slice 1 uses a single implicit rig.</summary>
public interface IEquipmentRepository
{
    Task<Equipment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Equipment>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Equipment equipment, CancellationToken cancellationToken = default);
}

public interface ICookRepository
{
    Task<Cook?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Cooks with no end time, newest first.</summary>
    Task<IReadOnlyList<Cook>> GetUnfinishedAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Cook cook, CancellationToken cancellationToken = default);
}

public interface ITempEntryRepository
{
    /// <summary>Meat readings for one cook, oldest first.</summary>
    Task<IReadOnlyList<TempEntry>> GetForCookAsync(Guid cookId, CancellationToken cancellationToken = default);

    Task SaveAsync(TempEntry entry, CancellationToken cancellationToken = default);
}

public interface IPitTempEntryRepository
{
    /// <summary>
    /// Pit readings for one rig within a time window, oldest first. Scoped to
    /// equipment rather than a cook because the fire belongs to the rig.
    /// </summary>
    Task<IReadOnlyList<PitTempEntry>> GetForEquipmentAsync(
        Guid equipmentId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task SaveAsync(PitTempEntry entry, CancellationToken cancellationToken = default);
}

internal sealed class EquipmentRepository : IEquipmentRepository
{
    private readonly IConnectionSource _connections;

    public EquipmentRepository(IConnectionSource connections) => _connections = connections;

    public async Task<Equipment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.Table<EquipmentRecord>()
            .Where(r => r.Id == id && r.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return record?.ToEntity();
    }

    public async Task<IReadOnlyList<Equipment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.Table<EquipmentRecord>()
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(Equipment equipment, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(equipment.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class CookRepository : ICookRepository
{
    private readonly IConnectionSource _connections;

    public CookRepository(IConnectionSource connections) => _connections = connections;

    public async Task<Cook?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.Table<CookRecord>()
            .Where(r => r.Id == id && r.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return record?.ToEntity();
    }

    public async Task<IReadOnlyList<Cook>> GetUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.Table<CookRecord>()
            .Where(r => r.FinishedAt == null && r.DeletedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(Cook cook, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(cook.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class TempEntryRepository : ITempEntryRepository
{
    private readonly IConnectionSource _connections;

    public TempEntryRepository(IConnectionSource connections) => _connections = connections;

    public async Task<IReadOnlyList<TempEntry>> GetForCookAsync(
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.Table<TempEntryRecord>()
            .Where(r => r.CookId == cookId && r.DeletedAt == null)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(TempEntry entry, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(entry.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class PitTempEntryRepository : IPitTempEntryRepository
{
    private readonly IConnectionSource _connections;

    public PitTempEntryRepository(IConnectionSource connections) => _connections = connections;

    public async Task<IReadOnlyList<PitTempEntry>> GetForEquipmentAsync(
        Guid equipmentId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.Table<PitTempEntryRecord>()
            .Where(r => r.EquipmentId == equipmentId
                        && r.DeletedAt == null
                        && r.RecordedAt >= from
                        && r.RecordedAt <= to)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(PitTempEntry entry, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(entry.ToRecord()).ConfigureAwait(false);
    }
}
