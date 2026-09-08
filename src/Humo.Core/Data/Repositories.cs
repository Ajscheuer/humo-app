using Humo.Core.Data.Records;
using Humo.Core.Identity;
using Humo.Shared.Entities;

namespace Humo.Core.Data;

/// <summary>Equipment reads and writes.</summary>
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

    /// <summary>
    /// Finished cooks, newest first. The history list.
    /// <para>
    /// No limit here on purpose. The free tier caps history, but that cap is a
    /// server-side policy value rather than a client constant, so it is applied
    /// where entitlements are known — not baked into the query.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Cook>> GetFinishedAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Cook cook, CancellationToken cancellationToken = default);
}

public interface ITempEntryRepository
{
    /// <summary>Meat readings for one cook, oldest first.</summary>
    Task<IReadOnlyList<TempEntry>> GetForCookAsync(Guid cookId, CancellationToken cancellationToken = default);

    Task SaveAsync(TempEntry entry, CancellationToken cancellationToken = default);
}

public interface IFuelEventRepository
{
    /// <summary>
    /// Fuel history for one rig, oldest first. Scoped to equipment because the
    /// fire is the rig's, not any one cook's.
    /// </summary>
    Task<IReadOnlyList<FuelEvent>> GetForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent fuel event on this rig, or null. This is what the fuel
    /// sheet pre-fills from, so it is a query in its own right rather than
    /// "load the whole history and take the last" — on a rig with a season of
    /// cooks behind it, that difference is thousands of rows per tap.
    /// </summary>
    Task<FuelEvent?> GetMostRecentForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(FuelEvent fuelEvent, CancellationToken cancellationToken = default);
}

public interface IEventRepository
{
    /// <summary>Milestones for one cook, oldest first.</summary>
    Task<IReadOnlyList<Event>> GetForCookAsync(Guid cookId, CancellationToken cancellationToken = default);

    Task SaveAsync(Event cookEvent, CancellationToken cancellationToken = default);
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
    private readonly IAccountContext _account;

    public EquipmentRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<Equipment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var record = await db.Table<EquipmentRecord>()
            .Where(r => r.Id == id && r.AccountId == accountId && r.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return record?.ToEntity();
    }

    public async Task<IReadOnlyList<Equipment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<EquipmentRecord>()
            .Where(r => r.AccountId == accountId && r.DeletedAt == null)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(Equipment equipment, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        equipment.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(equipment.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class CookRepository : ICookRepository
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public CookRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<Cook?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var record = await db.Table<CookRecord>()
            .Where(r => r.Id == id && r.AccountId == accountId && r.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return record?.ToEntity();
    }

    public async Task<IReadOnlyList<Cook>> GetUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<CookRecord>()
            .Where(r => r.FinishedAt == null && r.AccountId == accountId && r.DeletedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task<IReadOnlyList<Cook>> GetFinishedAsync(CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<CookRecord>()
            .Where(r => r.FinishedAt != null && r.AccountId == accountId && r.DeletedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(Cook cook, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        cook.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(cook.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class TempEntryRepository : ITempEntryRepository
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public TempEntryRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<IReadOnlyList<TempEntry>> GetForCookAsync(
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<TempEntryRecord>()
            .Where(r => r.CookId == cookId && r.AccountId == accountId && r.DeletedAt == null)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(TempEntry entry, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        entry.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(entry.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class PitTempEntryRepository : IPitTempEntryRepository
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public PitTempEntryRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<IReadOnlyList<PitTempEntry>> GetForEquipmentAsync(
        Guid equipmentId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<PitTempEntryRecord>()
            .Where(r => r.EquipmentId == equipmentId
                        && r.AccountId == accountId
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
        entry.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(entry.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class FuelEventRepository : IFuelEventRepository
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public FuelEventRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<IReadOnlyList<FuelEvent>> GetForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<FuelEventRecord>()
            .Where(r => r.EquipmentId == equipmentId && r.AccountId == accountId && r.DeletedAt == null)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task<FuelEvent?> GetMostRecentForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var record = await db.Table<FuelEventRecord>()
            .Where(r => r.EquipmentId == equipmentId && r.AccountId == accountId && r.DeletedAt == null)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return record?.ToEntity();
    }

    public async Task SaveAsync(FuelEvent fuelEvent, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        fuelEvent.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(fuelEvent.ToRecord()).ConfigureAwait(false);
    }
}

internal sealed class EventRepository : IEventRepository
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public EventRepository(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    public async Task<IReadOnlyList<Event>> GetForCookAsync(
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;
        var records = await db.Table<EventRecord>()
            .Where(r => r.CookId == cookId && r.AccountId == accountId && r.DeletedAt == null)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return records.Select(r => r.ToEntity()).ToList();
    }

    public async Task SaveAsync(Event cookEvent, CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        cookEvent.AccountId = _account.CurrentAccountId;
        await db.InsertOrReplaceAsync(cookEvent.ToRecord()).ConfigureAwait(false);
    }
}
