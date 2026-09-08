using Humo.Api.Tests.Support;
using Humo.Shared.Enums;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Sync;

public class SyncPushTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_batch_is_applied_parents_before_children()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var reading = _ctx.AReading(cook.Id);

        var response = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [reading],
        });

        // The shape of the request carries the ordering, so the normal case
        // cannot produce an orphan.
        Assert.Equal(3, response.Accepted);
        Assert.False(response.HasRejections);
        Assert.Equal(1, await _ctx.Db.Equipment.CountAsync());
        Assert.Equal(1, await _ctx.Db.Cooks.CountAsync());
        Assert.Equal(1, await _ctx.Db.TempEntries.CountAsync());
    }

    [Fact]
    public async Task Replaying_the_same_batch_changes_nothing()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var reading = _ctx.AReading(cook.Id);

        var request = new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [reading],
        };

        var first = await _ctx.PushAsync(request);
        var second = await _ctx.PushAsync(request);

        // Idempotence is the whole retry story: a client that loses the response
        // and re-sends must not double anything.
        Assert.Equal(3, first.Accepted);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(1, await _ctx.Db.Equipment.CountAsync());
        Assert.Equal(1, await _ctx.Db.Cooks.CountAsync());
        Assert.Equal(1, await _ctx.Db.TempEntries.CountAsync());
    }

    [Fact]
    public async Task A_replay_does_not_burn_a_sequence_number()
    {
        var rig = _ctx.ARig();
        var request = new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] };

        await _ctx.PushAsync(request);
        var sequenceAfterFirst = (await _ctx.Db.Equipment.SingleAsync()).Sequence;

        await _ctx.PushAsync(request);

        // Otherwise every retry would make every other device re-pull a record
        // that did not change.
        Assert.Equal(sequenceAfterFirst, (await _ctx.Db.Equipment.SingleAsync()).Sequence);
    }

    [Fact]
    public async Task An_orphaned_child_is_rejected_as_retryable_not_dropped()
    {
        var reading = _ctx.AReading(Guid.NewGuid());

        var response = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            TempEntries = [reading],
        });

        // Never held server-side, never silently dropped: the client re-sends it
        // with its parent.
        var rejection = Assert.Single(response.Rejected);
        Assert.Equal(reading.Id, rejection.RecordId);
        Assert.Equal(SyncRejectionReason.ParentMissing, rejection.Reason);
        Assert.True(rejection.IsRetryable);
        Assert.Equal(0, await _ctx.Db.TempEntries.CountAsync());
    }

    [Fact]
    public async Task An_orphan_is_accepted_once_its_parent_arrives()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var reading = _ctx.AReading(cook.Id);

        // A partial batch: the child made it, the parents did not.
        var first = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            TempEntries = [reading],
        });
        Assert.True(first.HasRejections);

        // The client re-sends it with its parents, as the rejection told it to.
        var second = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [reading],
        });

        Assert.False(second.HasRejections);
        Assert.Equal(3, second.Accepted);
    }

    [Fact]
    public async Task A_parent_earlier_in_the_same_batch_counts()
    {
        var rig = _ctx.ARig();

        // The cook's parent is not in the database yet -- it is two collections
        // up in this very request.
        var response = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [_ctx.ACook(rig.Id)],
        });

        Assert.False(response.HasRejections);
    }

    [Fact]
    public async Task A_later_edit_wins_over_an_earlier_one()
    {
        var rig = _ctx.ARig("Brazos");
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var edited = _ctx.ARig("Brazos (rebuilt)", updatedAt: rig.UpdatedAt.AddHours(1));
        edited.Id = rig.Id;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [edited] });

        Assert.Equal("Brazos (rebuilt)", (await _ctx.Db.Equipment.SingleAsync()).Name);
    }

    [Fact]
    public async Task An_older_edit_arriving_late_does_not_win()
    {
        var rig = _ctx.ARig("Brazos");
        var newer = _ctx.ARig("Newer", updatedAt: rig.UpdatedAt.AddHours(2));
        newer.Id = rig.Id;
        var older = _ctx.ARig("Older", updatedAt: rig.UpdatedAt.AddHours(1));
        older.Id = rig.Id;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [newer] });

        // A second device syncing late with an edit made earlier. Out-of-order
        // arrival must not resurrect the stale version.
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.SecondDevice, Equipment = [older] });

        Assert.Equal("Newer", (await _ctx.Db.Equipment.SingleAsync()).Name);
    }

    [Fact]
    public async Task Last_write_wins_replaces_the_whole_record_not_field_by_field()
    {
        var rig = _ctx.ARig("Brazos");
        rig.Notes = "Original note";
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var edited = _ctx.ARig("Brazos", updatedAt: rig.UpdatedAt.AddHours(1));
        edited.Id = rig.Id;
        edited.Notes = null;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [edited] });

        // Record granularity, stated in data-model.md 5.2: the losing version is
        // discarded, not merged, so the cleared note stays cleared.
        Assert.Null((await _ctx.Db.Equipment.SingleAsync()).Notes);
    }

    [Fact]
    public async Task An_append_only_record_is_never_overwritten()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var reading = _ctx.AReading(cook.Id, meatTempC: 68);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [reading],
        });

        var tampered = _ctx.AReading(cook.Id, meatTempC: 999, updatedAt: reading.UpdatedAt.AddHours(1));
        tampered.Id = reading.Id;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, TempEntries = [tampered] });

        // Corrections go through tombstone-plus-replacement, not by editing in
        // place. Insert-by-id means a same-id resend is a no-op.
        Assert.Equal(68, (await _ctx.Db.TempEntries.SingleAsync()).MeatTempC);
    }

    [Fact]
    public async Task A_tombstone_for_an_append_only_record_is_applied()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var reading = _ctx.AReading(cook.Id);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [reading],
        });

        var deleted = _ctx.AReading(cook.Id, updatedAt: reading.UpdatedAt.AddHours(1));
        deleted.Id = reading.Id;
        deleted.DeletedAt = reading.UpdatedAt.AddHours(1);

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, TempEntries = [deleted] });

        // A deletion is the one case where a same-id resend is not a no-op --
        // otherwise "I typed 165 instead of 65" could never be undone.
        var stored = await _ctx.Db.TempEntries.SingleAsync();
        Assert.NotNull(stored.DeletedAt);
    }

    [Fact]
    public async Task Nothing_is_ever_physically_removed()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var deleted = _ctx.ARig(updatedAt: rig.UpdatedAt.AddHours(1));
        deleted.Id = rig.Id;
        deleted.DeletedAt = rig.UpdatedAt.AddHours(1);

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [deleted] });

        // The row is still there, tombstoned. Purge is a separate, deliberate
        // operation for account deletion.
        Assert.Equal(1, await _ctx.Db.Equipment.CountAsync());
        Assert.NotNull((await _ctx.Db.Equipment.SingleAsync()).DeletedAt);
    }
}
