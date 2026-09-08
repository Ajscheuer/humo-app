using Humo.Api.Tests.Support;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Sync;

/// <summary>
/// One account never sees or overwrites another's records. The account comes from
/// the token, so these are the tests that prove a hostile payload cannot reach
/// across.
/// </summary>
public class SyncIsolationTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task An_account_never_pulls_another_accounts_records()
    {
        var theirs = _ctx.ARig("Their rig");
        await _ctx.PushAsync(
            new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [theirs] },
            account: _ctx.OtherAccount);

        var pull = await _ctx.PullAsync(cursor: 0);

        Assert.Equal(0, pull.Count);
    }

    [Fact]
    public async Task The_account_in_the_payload_is_ignored_in_favour_of_the_token()
    {
        var rig = _ctx.ARig();

        // A client claiming to be somebody else.
        rig.AccountId = _ctx.OtherAccount;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var stored = await _ctx.Db.Equipment.SingleAsync();

        Assert.Equal(_ctx.Account, stored.AccountId);
    }

    [Fact]
    public async Task A_record_id_belonging_to_another_account_is_not_overwritten()
    {
        var theirs = _ctx.ARig("Theirs");
        await _ctx.PushAsync(
            new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [theirs] },
            account: _ctx.OtherAccount);

        // Same id, later timestamp: last-write-wins would clobber it if the
        // lookup were by id alone.
        var mine = _ctx.ARig("Mine", updatedAt: theirs.UpdatedAt.AddHours(1));
        mine.Id = theirs.Id;
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [mine] });

        var rows = await _ctx.Db.Equipment.ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Theirs", rows.Single(r => r.AccountId == _ctx.OtherAccount).Name);
        Assert.Equal("Mine", rows.Single(r => r.AccountId == _ctx.Account).Name);
    }

    [Fact]
    public async Task Another_accounts_record_does_not_satisfy_a_parent_reference()
    {
        var theirRig = _ctx.ARig();
        await _ctx.PushAsync(
            new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [theirRig] },
            account: _ctx.OtherAccount);

        // Pointing a cook at a rig this account cannot see. Accepting it would
        // let one account probe another's ids by watching what is rejected.
        var response = await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Cooks = [_ctx.ACook(theirRig.Id)],
        });

        Assert.Equal(SyncRejectionReason.ParentMissing, Assert.Single(response.Rejected).Reason);
        Assert.Equal(0, await _ctx.Db.Cooks.CountAsync(c => c.AccountId == _ctx.Account));
    }

    [Fact]
    public async Task Sequence_numbers_are_per_account_not_global()
    {
        await _ctx.PushAsync(
            new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [_ctx.ARig()] },
            account: _ctx.OtherAccount);

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [_ctx.ARig()] });

        // A shared counter would make one account's activity advance the other's
        // stream, so a quiet account's device would pull nothing while its
        // cursor climbed.
        var mine = await _ctx.Db.Equipment.SingleAsync(r => r.AccountId == _ctx.Account);

        Assert.Equal(1, mine.Sequence);
    }

    [Fact]
    public async Task One_device_id_used_by_two_accounts_keeps_two_cursors()
    {
        // Not contrived: two people signing in on the same phone. Each account's
        // records are written by the *other* device, so this one has something
        // to read in both streams.
        await _ctx.PushAsync(
            new SyncPushRequest { DeviceId = _ctx.SecondDevice, Equipment = [_ctx.ARig()] });

        await _ctx.PushAsync(
            new SyncPushRequest
            {
                DeviceId = _ctx.SecondDevice,
                Equipment = [_ctx.ARig(), _ctx.ARig("Second")],
            },
            account: _ctx.OtherAccount);

        await _ctx.PullAsync(cursor: 0);
        await _ctx.PullAsync(cursor: 0, account: _ctx.OtherAccount);

        var cursors = await _ctx.Db.DeviceCursors
            .Where(c => c.DeviceId == _ctx.Device)
            .ToListAsync();

        // One phone, two positions. A single cursor keyed on the device alone
        // would let one account's reading advance the other's.
        Assert.Equal(2, cursors.Count);
        Assert.Equal(1, cursors.Single(c => c.AccountId == _ctx.Account).Cursor);
        Assert.Equal(2, cursors.Single(c => c.AccountId == _ctx.OtherAccount).Cursor);
    }
}
