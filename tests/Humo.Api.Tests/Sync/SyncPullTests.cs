using Humo.Api.Sync;
using Humo.Api.Tests.Support;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Sync;

public class SyncPullTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_second_device_pulls_what_the_first_one_pushed()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        var pull = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        Assert.Equal(rig.Id, Assert.Single(pull.Equipment).Id);
        Assert.Equal(cook.Id, Assert.Single(pull.Cooks).Id);
    }

    [Fact]
    public async Task A_device_does_not_pull_back_its_own_push()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        // From the beginning of the stream, so nothing is hidden by a cursor:
        // the device is spared its own record because the row records who
        // wrote it.
        var pull = await _ctx.PullAsync(cursor: 0);

        Assert.Equal(0, pull.Count);
    }

    [Fact]
    public async Task A_record_written_right_after_this_devices_push_is_still_pulled()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        // A second device writes immediately afterwards, taking the very next
        // sequence number.
        var cook = _ctx.ACook(rig.Id);
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.SecondDevice, Cooks = [cook] });

        var pull = await _ctx.PullAsync(cursor: 0);

        Assert.Equal(cook.Id, Assert.Single(pull.Cooks).Id);
    }

    [Fact]
    public async Task Pushing_does_not_skip_records_written_before_the_push()
    {
        // The other phone was busy while this one was offline.
        var rig = _ctx.ARig("Bought while you were away");
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.SecondDevice, Equipment = [rig] });

        // This device comes back, pushes its own cook, and pulls from where it
        // actually is in the stream: still the beginning.
        var cook = _ctx.ACook(rig.Id);
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Cooks = [cook] });

        var pull = await _ctx.PullAsync(cursor: 0);

        // Advancing this device's cursor past its own write would step over
        // everything written before it, permanently.
        Assert.Contains(pull.Equipment, e => e.Id == rig.Id);
    }

    [Fact]
    public async Task A_device_pulls_only_what_it_did_not_write_itself()
    {
        var rig = _ctx.ARig("Mine");
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var theirs = _ctx.ARig("Theirs");
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.SecondDevice, Equipment = [theirs] });

        var pull = await _ctx.PullAsync(cursor: 0);

        // The device already has its own record, by definition. Skipping it is
        // what the writing device is recorded for.
        Assert.Equal(theirs.Id, Assert.Single(pull.Equipment).Id);
    }

    [Fact]
    public async Task Nothing_left_but_this_devices_own_records_is_not_more_to_pull()
    {
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [_ctx.ARig()] });

        var pull = await _ctx.PullAsync(cursor: 0);

        // Otherwise the client would loop, told there is more and handed nothing.
        Assert.Equal(0, pull.Count);
        Assert.False(pull.HasMore);
    }

    [Fact]
    public async Task Pulling_twice_with_the_returned_cursor_hands_back_nothing_new()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var first = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);
        var second = await _ctx.PullAsync(cursor: first.Cursor, device: _ctx.SecondDevice);

        Assert.Equal(1, first.Count);
        Assert.Equal(0, second.Count);
        Assert.Equal(first.Cursor, second.Cursor);
    }

    [Fact]
    public async Task An_empty_pull_leaves_the_cursor_where_it_was()
    {
        var pull = await _ctx.PullAsync(cursor: 7, device: _ctx.SecondDevice);

        // Not zero: a device with nothing to collect must not be rewound to the
        // beginning of the stream and re-handed the whole account.
        Assert.Equal(7, pull.Cursor);
        Assert.False(pull.HasMore);
    }

    [Fact]
    public async Task A_pull_only_reports_more_when_it_actually_truncated()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var pull = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        Assert.False(pull.HasMore);
    }

    [Fact]
    public async Task An_oversized_account_is_paged_and_says_so()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var readings = Enumerable
            .Range(0, SyncPolicy.MaxPullBatchSize + 10)
            .Select(i => _ctx.AReading(cook.Id, meatTempC: 60 + (i % 20)))
            .ToList();

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = readings,
        });

        var first = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        Assert.Equal(SyncPolicy.MaxPullBatchSize, first.Count);
        Assert.True(first.HasMore);

        // A device back after a month catches up by pulling again straight away,
        // not by waiting for the next sync.
        var second = await _ctx.PullAsync(cursor: first.Cursor, device: _ctx.SecondDevice);

        Assert.Equal(12, second.Count);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task A_truncated_page_never_carries_a_child_without_its_parent()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        var readings = Enumerable
            .Range(0, SyncPolicy.MaxPullBatchSize)
            .Select(_ => _ctx.AReading(cook.Id))
            .ToList();

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = readings,
        });

        var page = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        // The budget is spent parents-first, so the rig and the cook are on the
        // page that carries readings belonging to them.
        Assert.Single(page.Equipment);
        Assert.Single(page.Cooks);
        Assert.All(page.TempEntries, t => Assert.Equal(cook.Id, t.CookId));
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task A_tombstoned_record_is_pulled_rather_than_hidden()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var deleted = _ctx.ARig(updatedAt: rig.UpdatedAt.AddHours(1));
        deleted.Id = rig.Id;
        deleted.DeletedAt = rig.UpdatedAt.AddHours(1);
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [deleted] });

        var pull = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        // The other device has the live version and only learns of the deletion
        // by being handed the tombstone. Filtering deletes out of a pull would
        // make a delete unsyncable.
        Assert.NotNull(Assert.Single(pull.Equipment).DeletedAt);
    }

    [Fact]
    public async Task A_pull_records_where_the_device_has_read_to()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var pull = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        var stored = await _ctx.Db.DeviceCursors
            .SingleAsync(c => c.DeviceId == _ctx.SecondDevice);

        Assert.Equal(pull.Cursor, stored.Cursor);
    }

    [Fact]
    public async Task A_stale_cursor_does_not_rewind_a_device_that_has_read_further()
    {
        var rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var first = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        // A retried or reordered request arriving with an old cursor.
        await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        var stored = await _ctx.Db.DeviceCursors
            .SingleAsync(c => c.DeviceId == _ctx.SecondDevice);

        Assert.Equal(first.Cursor, stored.Cursor);
    }
}
