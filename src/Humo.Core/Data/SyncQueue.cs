using Humo.Core.Data.Records;
using Humo.Core.Identity;
using Humo.Shared.Entities;
using Humo.Shared.Sync;

namespace Humo.Core.Data;

/// <summary>
/// The device side of sync: what still has to go up, and what to do with what
/// comes down.
/// <para>
/// Separate from the feature repositories on purpose. They answer questions
/// about the current cook and must never be slowed by sync bookkeeping; this
/// answers one question — what has changed since the last successful push — and
/// it is the only thing that reads or writes <c>SyncedAt</c>.
/// </para>
/// </summary>
public interface ISyncQueue
{
    /// <summary>
    /// Up to <paramref name="budget"/> records not yet acknowledged by the
    /// server, shaped as a push batch with parents before children.
    /// </summary>
    /// <param name="budget">
    /// A cap, because a device offline for a season has thousands of records
    /// queued and one request carrying all of them would time out every time —
    /// leaving it permanently unable to sync. Spent parents-first, so a
    /// truncated batch drops children and never orphans one.
    /// </param>
    Task<SyncPushRequest> CollectAsync(
        Guid deviceId,
        int budget = SyncQueue.MaxPushBatchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the records in <paramref name="batch"/> as synced, except any the
    /// server rejected — those stay queued so the next push re-sends them with
    /// their parents.
    /// </summary>
    /// <param name="collectedAt">
    /// When <see cref="CollectAsync"/> read the batch, not when the server
    /// answered. A record edited while the push was in flight has an
    /// <c>UpdatedAt</c> after this, so it stays dirty and goes up next time —
    /// stamping "now" instead would mark a version the server never saw as
    /// synced and lose that edit silently.
    /// </param>
    Task AcknowledgeAsync(
        SyncPushRequest batch,
        IReadOnlyCollection<Guid> rejectedIds,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes what the server sent down, applying the same last-write-wins rule
    /// the server uses so both sides converge on the same answer.
    /// </summary>
    Task<int> ApplyAsync(SyncPullResponse response, DateTimeOffset syncedAt, CancellationToken cancellationToken = default);
}

internal sealed class SyncQueue : ISyncQueue
{
    private readonly IConnectionSource _connections;
    private readonly IAccountContext _account;

    public SyncQueue(IConnectionSource connections, IAccountContext account)
    {
        _connections = connections;
        _account = account;
    }

    /// <summary>
    /// The most records one push carries. Sized so a batch fits comfortably
    /// inside the transport timeout on a poor connection; a device with more
    /// than this queued sends several rounds rather than one that never lands.
    /// </summary>
    internal const int MaxPushBatchSize = 500;

    public async Task<SyncPushRequest> CollectAsync(
        Guid deviceId,
        int budget = MaxPushBatchSize,
        CancellationToken cancellationToken = default)
    {
        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;

        // Parents first, and the budget is spent in that order, so a truncated
        // batch drops children and never a parent something else needs.
        var equipment = await DirtyAsync<EquipmentRecord>(db, accountId, budget).ConfigureAwait(false);
        budget -= equipment.Count;

        var cooks = await DirtyAsync<CookRecord>(db, accountId, budget).ConfigureAwait(false);
        budget -= cooks.Count;

        var temps = await DirtyAsync<TempEntryRecord>(db, accountId, budget).ConfigureAwait(false);
        budget -= temps.Count;

        var pitTemps = await DirtyAsync<PitTempEntryRecord>(db, accountId, budget).ConfigureAwait(false);
        budget -= pitTemps.Count;

        var fuel = await DirtyAsync<FuelEventRecord>(db, accountId, budget).ConfigureAwait(false);
        budget -= fuel.Count;

        var events = await DirtyAsync<EventRecord>(db, accountId, budget).ConfigureAwait(false);

        return new SyncPushRequest
        {
            DeviceId = deviceId,
            Equipment = equipment.Select(r => r.ToEntity()).ToList(),
            Cooks = cooks.Select(r => r.ToEntity()).ToList(),
            TempEntries = temps.Select(r => r.ToEntity()).ToList(),
            PitTempEntries = pitTemps.Select(r => r.ToEntity()).ToList(),
            FuelEvents = fuel.Select(r => r.ToEntity()).ToList(),
            Events = events.Select(r => r.ToEntity()).ToList(),
        };
    }

    /// <summary>
    /// Records changed since the last acknowledgement, oldest first.
    /// <para>
    /// Dirty means either never pushed or edited afterwards. Comparing the two
    /// timestamps catches the second case, which a null check alone would miss.
    /// Oldest first so a device that never finishes catching up still makes
    /// forward progress in a stable order rather than resending the same page.
    /// </para>
    /// </summary>
    private static async Task<List<TRecord>> DirtyAsync<TRecord>(
        SQLite.SQLiteAsyncConnection db,
        Guid accountId,
        int budget)
        where TRecord : class, ISyncedRecord, new()
        => budget <= 0
            ? []
            : await db.Table<TRecord>()
                .Where(r => r.AccountId == accountId && (r.SyncedAt == null || r.SyncedAt < r.UpdatedAt))
                .OrderBy(r => r.UpdatedAt)
                .Take(budget)
                .ToListAsync()
                .ConfigureAwait(false);

    public async Task AcknowledgeAsync(
        SyncPushRequest batch,
        IReadOnlyCollection<Guid> rejectedIds,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rejectedIds);

        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;

        var accepted = batch.InApplyOrder()
            .Where(e => !rejectedIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToList();

        if (accepted.Count == 0)
        {
            return;
        }

        foreach (var table in SyncTables.All)
        {
            // Chunked because SQLite caps the number of parameters in a
            // statement, and a first sync after a season offline can be
            // thousands of rows.
            foreach (var chunk in accepted.Chunk(200))
            {
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                var arguments = new object[] { collectedAt, accountId }
                    .Concat(chunk.Select(id => (object)id))
                    .ToArray();

                // Table names come from the constant list, never from input.
                await db.ExecuteAsync(
                    $"UPDATE {table} SET SyncedAt = ? WHERE AccountId = ? AND Id IN ({placeholders})",
                    arguments).ConfigureAwait(false);
            }
        }
    }

    public async Task<int> ApplyAsync(
        SyncPullResponse response,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var accountId = _account.CurrentAccountId;

        var applied = 0;

        // Parents before children, matching the order they came down in, so a
        // child never lands against a parent this device has not written yet.
        foreach (var e in response.Equipment)
        {
            applied += await ApplyOneAsync<EquipmentRecord>(
                db, e, accountId, syncedAt, () => e.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        foreach (var c in response.Cooks)
        {
            applied += await ApplyOneAsync<CookRecord>(
                db, c, accountId, syncedAt, () => c.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        foreach (var t in response.TempEntries)
        {
            applied += await ApplyOneAsync<TempEntryRecord>(
                db, t, accountId, syncedAt, () => t.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        foreach (var p in response.PitTempEntries)
        {
            applied += await ApplyOneAsync<PitTempEntryRecord>(
                db, p, accountId, syncedAt, () => p.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        foreach (var f in response.FuelEvents)
        {
            applied += await ApplyOneAsync<FuelEventRecord>(
                db, f, accountId, syncedAt, () => f.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        foreach (var ev in response.Events)
        {
            applied += await ApplyOneAsync<EventRecord>(
                db, ev, accountId, syncedAt, () => ev.ToRecord(syncedAt)).ConfigureAwait(false);
        }

        return applied;
    }

    /// <summary>
    /// Writes one incoming record unless the device holds a newer version of it.
    /// </summary>
    private static async Task<int> ApplyOneAsync<TRecord>(
        SQLite.SQLiteAsyncConnection db,
        Entity incoming,
        Guid accountId,
        DateTimeOffset syncedAt,
        Func<TRecord> toRecord)
        where TRecord : class, ISyncedRecord, new()
    {
        var existing = await db.FindAsync<TRecord>(incoming.Id).ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.AccountId != accountId)
            {
                // Another account's row under the same id. Never overwrite it:
                // whichever account is signed in now, the other one's data is
                // not this sync's to touch.
                return 0;
            }

            // Same rule as the server's, so both sides land on the same version
            // rather than ping-ponging edits at each other. Equal loses, which
            // makes a re-pull of an unchanged record a no-op.
            if (incoming.UpdatedAt <= existing.UpdatedAt)
            {
                return 0;
            }
        }

        var record = toRecord();
        record.AccountId = accountId;

        // Never behind the record's own UpdatedAt. A record written on a device
        // with a fast clock would otherwise read as dirty the moment it landed
        // and be pushed straight back to the server it just came from.
        record.SyncedAt = incoming.UpdatedAt > syncedAt ? incoming.UpdatedAt : syncedAt;

        await db.InsertOrReplaceAsync(record).ConfigureAwait(false);
        return 1;
    }
}

/// <summary>The tables sync writes to, by SQLite table name.</summary>
internal static class SyncTables
{
    public static readonly IReadOnlyList<string> All = RecordOwnership.OwnedTables;
}
