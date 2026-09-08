using Humo.Api.Sync;
using Humo.Api.Tests.Support;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Sync;

/// <summary>
/// Client clocks are not trustworthy, and last-write-wins turns on them. The
/// policy is accept, record, flag — never clamp, never reject.
/// </summary>
public class SyncClockTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_record_synced_promptly_is_not_flagged()
    {
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [_ctx.ARig()] });

        Assert.False((await _ctx.Db.Equipment.SingleAsync()).ClockSkewFlagged);
    }

    [Fact]
    public async Task A_cook_logged_offline_and_synced_a_day_later_is_not_flagged()
    {
        var rig = _ctx.ARig();

        // Airplane mode at the pit, sync when the phone finds wifi. Normal, not a
        // fault: flagging this would flag half the app's real usage.
        _ctx.Time.Advance(TimeSpan.FromHours(20));

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        Assert.False((await _ctx.Db.Equipment.SingleAsync()).ClockSkewFlagged);
    }

    [Fact]
    public async Task A_client_clock_wrong_by_months_is_flagged()
    {
        var rig = _ctx.ARig(updatedAt: _ctx.Time.GetUtcNow().AddDays(-90));

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        Assert.True((await _ctx.Db.Equipment.SingleAsync()).ClockSkewFlagged);
    }

    [Fact]
    public async Task A_client_clock_set_into_the_future_is_flagged_too()
    {
        // Divergence is absolute. A phone a year ahead would otherwise win every
        // last-write-wins contest forever, unremarked.
        var rig = _ctx.ARig(updatedAt: _ctx.Time.GetUtcNow().AddDays(365));

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        Assert.True((await _ctx.Db.Equipment.SingleAsync()).ClockSkewFlagged);
    }

    [Fact]
    public async Task A_flagged_record_is_still_stored_and_still_pulled()
    {
        var rig = _ctx.ARig("Wrong clock", updatedAt: _ctx.Time.GetUtcNow().AddDays(-400));

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var pull = await _ctx.PullAsync(cursor: 0, device: _ctx.SecondDevice);

        // Flagging is a note for analytics, not a filter. Dropping the record
        // would lose a cook the user actually did.
        Assert.Equal("Wrong clock", Assert.Single(pull.Equipment).Name);
    }

    [Fact]
    public async Task A_skewed_timestamp_is_stored_as_sent_not_clamped()
    {
        var loggedAt = _ctx.Time.GetUtcNow().AddDays(-120);
        var rig = _ctx.ARig(updatedAt: loggedAt);

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [rig] });

        var stored = await _ctx.Db.Equipment.SingleAsync();

        // Clamping to receipt time would collapse a multi-day cook into one
        // instant and wreck every interval the fire model reads from it.
        Assert.Equal(loggedAt, stored.UpdatedAt);
        Assert.Equal(_ctx.Time.GetUtcNow(), stored.ReceivedAt);
    }

    [Fact]
    public async Task Receipt_time_does_not_decide_last_write_wins()
    {
        var rig = _ctx.ARig("First");
        var later = _ctx.ARig("Edited later", updatedAt: rig.UpdatedAt.AddHours(5));
        later.Id = rig.Id;

        // The later edit reaches the server first, from a device that had signal.
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [later] });

        _ctx.Time.Advance(TimeSpan.FromHours(3));
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.SecondDevice, Equipment = [rig] });

        // Arrival order is an accident of connectivity. The client's updatedAt
        // decides, as data-model.md 5.2 says.
        Assert.Equal("Edited later", (await _ctx.Db.Equipment.SingleAsync()).Name);
    }

    [Fact]
    public void The_skew_threshold_is_generous_enough_for_a_weekend_offline()
    {
        // Guards the number itself: an overnight brisket plus a day before the
        // phone finds wifi must sit inside it.
        Assert.True(SyncPolicy.ClockSkewThreshold >= TimeSpan.FromHours(24));
    }
}
