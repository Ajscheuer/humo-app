using Humo.Api.Data;
using Humo.Shared.Entities;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Sync;

/// <summary>Applying batches and serving changes. The merge rules live here.</summary>
public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(
        Guid accountId,
        SyncPushRequest request,
        CancellationToken cancellationToken = default);

    Task<SyncPullResponse> PullAsync(
        Guid accountId,
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken = default);
}

public sealed class SyncService : ISyncService
{
    private readonly HumoDbContext _db;
    private readonly TimeProvider _time;

    public SyncService(HumoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<SyncPushResponse> PushAsync(
        Guid accountId,
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var receivedAt = _time.GetUtcNow();
        var rejections = new List<SyncRejection>();
        var accepted = 0;

        // Ids that exist for this account after this batch, used to resolve
        // parents. Seeded from what is already stored and extended as the batch
        // is applied, so a child later in the batch sees a parent earlier in it.
        var equipmentIds = await _db.Equipment
            .Where(r => r.AccountId == accountId)
            .Select(r => r.Id)
            .ToHashSetAsync(cancellationToken);

        var cookIds = await _db.Cooks
            .Where(r => r.AccountId == accountId)
            .Select(r => r.Id)
            .ToHashSetAsync(cancellationToken);

        var sequence = await NextSequenceAsync(accountId, cancellationToken);

        // Equipment has no parent.
        foreach (var equipment in request.Equipment)
        {
            if (await UpsertMutableAsync<EquipmentRow, Equipment>(
                    _db.Equipment, equipment, accountId, request.DeviceId, receivedAt,
                    (row, e) => row.CopyFrom(e), () => sequence++, cancellationToken))
            {
                accepted++;
            }

            // Whether or not this version won, the rig now exists for this
            // account, so a child later in the batch is not an orphan.
            equipmentIds.Add(equipment.Id);
        }

        foreach (var cook in request.Cooks)
        {
            if (!equipmentIds.Contains(cook.EquipmentId))
            {
                rejections.Add(Reject(cook.Id, nameof(Cook)));
                continue;
            }

            if (await UpsertMutableAsync<CookRow, Cook>(
                    _db.Cooks, cook, accountId, request.DeviceId, receivedAt,
                    (row, c) => row.CopyFrom(c), () => sequence++, cancellationToken))
            {
                accepted++;
            }

            cookIds.Add(cook.Id);
        }

        foreach (var entry in request.TempEntries)
        {
            if (!cookIds.Contains(entry.CookId))
            {
                rejections.Add(Reject(entry.Id, nameof(TempEntry)));
                continue;
            }

            if (await InsertAppendOnlyAsync<TempEntryRow, TempEntry>(
                    _db.TempEntries, entry, accountId, request.DeviceId, receivedAt,
                    (row, t) => row.CopyFrom(t), () => sequence++, cancellationToken))
            {
                accepted++;
            }
        }

        foreach (var entry in request.PitTempEntries)
        {
            if (!equipmentIds.Contains(entry.EquipmentId))
            {
                rejections.Add(Reject(entry.Id, nameof(PitTempEntry)));
                continue;
            }

            if (await InsertAppendOnlyAsync<PitTempEntryRow, PitTempEntry>(
                    _db.PitTempEntries, entry, accountId, request.DeviceId, receivedAt,
                    (row, p) => row.CopyFrom(p), () => sequence++, cancellationToken))
            {
                accepted++;
            }
        }

        foreach (var fuel in request.FuelEvents)
        {
            if (!equipmentIds.Contains(fuel.EquipmentId))
            {
                rejections.Add(Reject(fuel.Id, nameof(FuelEvent)));
                continue;
            }

            if (await InsertAppendOnlyAsync<FuelEventRow, FuelEvent>(
                    _db.FuelEvents, fuel, accountId, request.DeviceId, receivedAt,
                    (row, f) => row.CopyFrom(f), () => sequence++, cancellationToken))
            {
                accepted++;
            }
        }

        foreach (var milestone in request.Events)
        {
            if (!cookIds.Contains(milestone.CookId))
            {
                rejections.Add(Reject(milestone.Id, nameof(Event)));
                continue;
            }

            if (await InsertAppendOnlyAsync<EventRow, Event>(
                    _db.Events, milestone, accountId, request.DeviceId, receivedAt,
                    (row, e) => row.CopyFrom(e), () => sequence++, cancellationToken))
            {
                accepted++;
            }
        }

        await SaveSequenceAsync(accountId, sequence, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Deliberately no cursor here. A push does not move the device forward
        // in the stream: the records it just wrote are skipped on pull because
        // the row records which device wrote them, and nudging the cursor to
        // the batch's high-water mark instead would step over everything
        // another device wrote before this push — permanently, and silently.
        return new SyncPushResponse
        {
            Accepted = accepted,
            Rejected = rejections,
        };
    }

    public async Task<SyncPullResponse> PullAsync(
        Guid accountId,
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken = default)
    {
        // Parents first, and the budget is spent in that order, so a truncated
        // page never contains a child whose parent is on the next one.
        var budget = SyncPolicy.MaxPullBatchSize;

        var equipment = await PageAsync(_db.Equipment, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= equipment.Count;

        var cooks = await PageAsync(_db.Cooks, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= cooks.Count;

        var tempEntries = await PageAsync(_db.TempEntries, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= tempEntries.Count;

        var pitEntries = await PageAsync(_db.PitTempEntries, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= pitEntries.Count;

        var fuelEvents = await PageAsync(_db.FuelEvents, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= fuelEvents.Count;

        var events = await PageAsync(_db.Events, accountId, deviceId, cursor, budget, cancellationToken);
        budget -= events.Count;

        var returned = new SyncedRow[][]
                { [.. equipment], [.. cooks], [.. tempEntries], [.. pitEntries], [.. fuelEvents], [.. events] }
            .SelectMany(rows => rows)
            .ToList();

        // The high-water mark of what was actually returned. Advancing past
        // anything not sent would silently skip it forever.
        var newCursor = returned.Count > 0 ? returned.Max(r => r.Sequence) : cursor;

        var remaining = await CountRemainingAsync(accountId, deviceId, newCursor, cancellationToken);

        await AdvanceCursorAsync(accountId, deviceId, newCursor, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncPullResponse
        {
            Equipment = equipment.Select(r => r.ToEntity()).ToList(),
            Cooks = cooks.Select(r => r.ToEntity()).ToList(),
            TempEntries = tempEntries.Select(r => r.ToEntity()).ToList(),
            PitTempEntries = pitEntries.Select(r => r.ToEntity()).ToList(),
            FuelEvents = fuelEvents.Select(r => r.ToEntity()).ToList(),
            Events = events.Select(r => r.ToEntity()).ToList(),
            Cursor = newCursor,
            HasMore = remaining > 0,
        };
    }

    private static SyncRejection Reject(Guid id, string type) => new()
    {
        RecordId = id,
        RecordType = type,
        Reason = SyncRejectionReason.ParentMissing,
    };

    /// <summary>
    /// Insert-by-id. Re-sending the same record is a no-op, which is what makes a
    /// replayed batch produce the same state as a single delivery.
    /// </summary>
    private async Task<bool> InsertAppendOnlyAsync<TRow, TEntity>(
        DbSet<TRow> set,
        TEntity entity,
        Guid accountId,
        Guid deviceId,
        DateTimeOffset receivedAt,
        Action<TRow, TEntity> copy,
        Func<long> nextSequence,
        CancellationToken cancellationToken)
        where TRow : SyncedRow, new()
        where TEntity : Entity
    {
        var existing = await set.FirstOrDefaultAsync(
            r => r.Id == entity.Id && r.AccountId == accountId, cancellationToken);

        if (existing is not null)
        {
            // Already here. One exception: a tombstone arriving for a record the
            // server holds as live is a deletion, not a duplicate.
            if (entity.DeletedAt is not null && existing.DeletedAt is null)
            {
                existing.DeletedAt = entity.DeletedAt;
                existing.UpdatedAt = entity.UpdatedAt;
                existing.DeviceId = deviceId;
                existing.Sequence = nextSequence();
                return true;
            }

            return false;
        }

        var row = new TRow();
        row.ApplyEnvelope(entity, accountId, deviceId, receivedAt);
        copy(row, entity);
        row.Sequence = nextSequence();

        set.Add(row);
        return true;
    }

    /// <summary>
    /// Last-write-wins at record granularity, comparing the client's
    /// <c>updatedAt</c>. The losing version is discarded rather than merged
    /// field by field — stated in data-model.md §5.2 so nobody is surprised
    /// when a notes edit on one device loses to a rating edit on another.
    /// </summary>
    /// <returns>True when the record changed server state.</returns>
    private async Task<bool> UpsertMutableAsync<TRow, TEntity>(
        DbSet<TRow> set,
        TEntity entity,
        Guid accountId,
        Guid deviceId,
        DateTimeOffset receivedAt,
        Action<TRow, TEntity> copy,
        Func<long> nextSequence,
        CancellationToken cancellationToken)
        where TRow : SyncedRow, new()
        where TEntity : Entity
    {
        var existing = await set.FirstOrDefaultAsync(
            r => r.Id == entity.Id && r.AccountId == accountId, cancellationToken);

        if (existing is null)
        {
            var row = new TRow();
            row.ApplyEnvelope(entity, accountId, deviceId, receivedAt);
            copy(row, entity);
            row.Sequence = nextSequence();
            set.Add(row);
            return true;
        }

        // Strictly newer, not newer-or-equal: an identical replay must not burn a
        // sequence number, or every retry would make every other device re-pull
        // a record that did not change.
        if (entity.UpdatedAt <= existing.UpdatedAt)
        {
            return false;
        }

        existing.ApplyEnvelope(entity, accountId, deviceId, receivedAt);
        copy(existing, entity);
        existing.Sequence = nextSequence();
        return true;
    }

    /// <summary>
    /// One page of the account's stream after <paramref name="cursor"/>, minus
    /// whatever this device wrote itself — it already has those.
    /// </summary>
    private static async Task<List<TRow>> PageAsync<TRow>(
        DbSet<TRow> set,
        Guid accountId,
        Guid deviceId,
        long cursor,
        int budget,
        CancellationToken cancellationToken)
        where TRow : SyncedRow
        => budget <= 0
            ? []
            : await set
                .Where(r => r.AccountId == accountId && r.Sequence > cursor && r.DeviceId != deviceId)
                .OrderBy(r => r.Sequence)
                .Take(budget)
                .ToListAsync(cancellationToken);

    /// <summary>
    /// What is left for this device after <paramref name="cursor"/>. Excludes
    /// its own writes on the same terms the page does, or a device whose only
    /// remaining rows are its own would be told forever that there is more and
    /// handed nothing.
    /// </summary>
    private async Task<int> CountRemainingAsync(
        Guid accountId,
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken)
    {
        var total = 0;

        total += await Remaining(_db.Equipment);
        total += await Remaining(_db.Cooks);
        total += await Remaining(_db.TempEntries);
        total += await Remaining(_db.PitTempEntries);
        total += await Remaining(_db.FuelEvents);
        total += await Remaining(_db.Events);

        return total;

        Task<int> Remaining<TRow>(DbSet<TRow> set)
            where TRow : SyncedRow
            => set.CountAsync(
                r => r.AccountId == accountId && r.Sequence > cursor && r.DeviceId != deviceId,
                cancellationToken);
    }

    private async Task<long> NextSequenceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var row = await _db.AccountSequences
            .FirstOrDefaultAsync(s => s.AccountId == accountId, cancellationToken);

        return (row?.LastSequence ?? 0) + 1;
    }

    private async Task SaveSequenceAsync(Guid accountId, long next, CancellationToken cancellationToken)
    {
        var row = await _db.AccountSequences
            .FirstOrDefaultAsync(s => s.AccountId == accountId, cancellationToken);

        if (row is null)
        {
            _db.AccountSequences.Add(new AccountSequenceRow
            {
                AccountId = accountId,
                LastSequence = next - 1,
            });
            return;
        }

        row.LastSequence = next - 1;
    }

    private async Task<long> AdvanceCursorAsync(
        Guid accountId,
        Guid deviceId,
        long cursor,
        CancellationToken cancellationToken)
    {
        var row = await _db.DeviceCursors.FirstOrDefaultAsync(
            c => c.AccountId == accountId && c.DeviceId == deviceId, cancellationToken);

        if (row is null)
        {
            _db.DeviceCursors.Add(new DeviceCursorRow
            {
                AccountId = accountId,
                DeviceId = deviceId,
                Cursor = cursor,
                UpdatedAt = _time.GetUtcNow(),
            });
            return cursor;
        }

        // Never backwards. A retried or out-of-order request carrying a stale
        // cursor must not rewind a device that has already read further.
        if (cursor > row.Cursor)
        {
            row.Cursor = cursor;
            row.UpdatedAt = _time.GetUtcNow();
        }

        return row.Cursor;
    }
}
