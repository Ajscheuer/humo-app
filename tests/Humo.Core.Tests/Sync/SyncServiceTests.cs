using Humo.Core.Sync;
using Humo.Core.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;

namespace Humo.Core.Tests.Sync;

public class SyncServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly StubSyncClient _client = new();
    private readonly InMemoryPreferences _preferences = new();
    private readonly ISyncState _state;
    private readonly ISyncService _sync;

    public SyncServiceTests()
    {
        _state = new SyncState(_preferences);

        // Signed in: the guest path is its own test below.
        _db.Account.SetCurrent(_db.Account.CurrentAccountId, isAnonymous: false);

        _sync = new Humo.Core.Sync.SyncService(_db.Queue, _client, _state, _db.Account, _db.Clock);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_guest_does_not_sync()
    {
        _db.Account.SetCurrent(_db.Account.CurrentAccountId, isAnonymous: true);
        await ARigAsync();

        var result = await _sync.SyncAsync();

        // Not a failure. "Continue without an account" means there is no account
        // on the server to sync to, and the local database is the source of
        // truth regardless.
        Assert.Equal(SyncOutcome.NotSignedIn, result.Outcome);
        Assert.Empty(_client.Pushes);
    }

    [Fact]
    public async Task A_queued_record_goes_up()
    {
        var rig = await ARigAsync();

        var result = await _sync.SyncAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Pushed);
        Assert.Equal(rig.Id, Assert.Single(Assert.Single(_client.Pushes).Equipment).Id);
    }

    [Fact]
    public async Task Syncing_twice_does_not_send_the_same_record_twice()
    {
        await ARigAsync();

        await _sync.SyncAsync();
        var second = await _sync.SyncAsync();

        Assert.Equal(SyncOutcome.NothingToDo, second.Outcome);
        Assert.Single(_client.Pushes);
    }

    [Fact]
    public async Task An_empty_database_still_pulls()
    {
        var result = await _sync.SyncAsync();

        // A device with nothing to say still has to hear what the other one did.
        Assert.Empty(_client.Pushes);
        Assert.Single(_client.Pulls);
        Assert.Equal(SyncOutcome.NothingToDo, result.Outcome);
    }

    [Fact]
    public async Task An_unreachable_server_leaves_the_queue_intact()
    {
        await ARigAsync();
        _client.PushFailure = SyncTransportFailure.Unreachable;

        var result = await _sync.SyncAsync();

        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        Assert.Equal(1, (await _db.Queue.CollectAsync(_state.DeviceId)).Count);
    }

    [Fact]
    public async Task A_failed_push_does_not_go_on_to_pull()
    {
        await ARigAsync();
        _client.PushFailure = SyncTransportFailure.Unreachable;

        await _sync.SyncAsync();

        // The same connection just failed; a pull would only fail behind it.
        Assert.Empty(_client.Pulls);
    }

    [Fact]
    public async Task A_refused_token_reports_that_rather_than_offline()
    {
        await ARigAsync();
        _client.PushFailure = SyncTransportFailure.Unauthorized;

        var result = await _sync.SyncAsync();

        // Different remedy: retrying later fixes offline, and only a sign-in
        // fixes this.
        Assert.Equal(SyncOutcome.NotSignedIn, result.Outcome);
    }

    [Fact]
    public async Task A_rejected_record_is_reported_and_stays_queued()
    {
        var rig = await ARigAsync();
        _client.Reject.Add(rig.Id);

        var result = await _sync.SyncAsync();

        Assert.Equal(SyncOutcome.PartiallyApplied, result.Outcome);
        Assert.Equal(1, result.Deferred);
        Assert.Equal(0, result.Pushed);
        Assert.Equal(1, (await _db.Queue.CollectAsync(_state.DeviceId)).Count);
    }

    [Fact]
    public async Task A_partly_rejected_batch_still_banks_what_was_accepted()
    {
        var rig = await ARigAsync();
        var cook = await ACookAsync(rig.Id);
        _client.Reject.Add(cook.Id);

        var result = await _sync.SyncAsync();

        Assert.Equal(1, result.Pushed);
        Assert.Equal(1, result.Deferred);

        var stillQueued = await _db.Queue.CollectAsync(_state.DeviceId);

        Assert.Empty(stillQueued.Equipment);
        Assert.Equal(cook.Id, Assert.Single(stillQueued.Cooks).Id);
    }

    [Fact]
    public async Task A_pulled_record_is_written_locally()
    {
        var incoming = ARig("From the other phone");
        _client.PullResponse = new SyncPullResponse
        {
            Equipment = [incoming],
            Cursor = 5,
            HasMore = false,
        };

        var result = await _sync.SyncAsync();

        Assert.Equal(1, result.Pulled);
        Assert.Equal("From the other phone", (await _db.Equipment.GetAsync(incoming.Id))?.Name);
    }

    [Fact]
    public async Task The_cursor_advances_so_the_next_pull_asks_for_less()
    {
        _client.PullResponse = new SyncPullResponse { Cursor = 12, HasMore = false };

        await _sync.SyncAsync();
        await _sync.SyncAsync();

        Assert.Equal(0, _client.Pulls[0].Cursor);
        Assert.Equal(12, _client.Pulls[1].Cursor);
    }

    [Fact]
    public async Task A_failed_pull_does_not_advance_the_cursor()
    {
        _client.PullFailure = SyncTransportFailure.Unreachable;

        await _sync.SyncAsync();
        _client.PullFailure = null;
        await _sync.SyncAsync();

        Assert.Equal(0, _client.Pulls[1].Cursor);
    }

    [Fact]
    public async Task A_failed_pull_still_counts_what_the_push_achieved()
    {
        await ARigAsync();
        _client.PullFailure = SyncTransportFailure.Unreachable;

        var result = await _sync.SyncAsync();

        // Those records are on the server and acknowledged. Reporting zero would
        // invite a caller to treat the whole round as lost.
        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        Assert.Equal(1, result.Pushed);
        Assert.Equal(0, (await _db.Queue.CollectAsync(_state.DeviceId)).Count);
    }

    [Fact]
    public async Task More_waiting_on_the_server_is_reported()
    {
        _client.PullResponse = new SyncPullResponse { Cursor = 500, HasMore = true };

        var result = await _sync.SyncAsync();

        // The caller pulls again straight away rather than waiting for the next
        // sync: a device back after a month should not need a month of launches.
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task A_device_with_more_queued_than_one_batch_sends_several()
    {
        var rig = await ARigAsync();
        var cook = await ACookAsync(rig.Id);

        // A season logged offline: more readings than one request may carry.
        for (var i = 0; i < 620; i++)
        {
            await _db.TempEntries.SaveAsync(AReading(cook.Id));
        }

        var result = await _sync.SyncAsync();

        // One round would leave the rest queued until the next launch, which for
        // a phone that syncs on resume could be days.
        Assert.True(_client.Pushes.Count > 1, "Expected the queue to be sent in more than one batch.");
        Assert.Equal(622, result.Pushed);
        Assert.Equal(0, (await _db.Queue.CollectAsync(_state.DeviceId)).Count);
    }

    [Fact]
    public async Task A_truncated_batch_still_carries_parents_before_children()
    {
        var rig = await ARigAsync();
        var cook = await ACookAsync(rig.Id);

        for (var i = 0; i < 620; i++)
        {
            await _db.TempEntries.SaveAsync(AReading(cook.Id));
        }

        await _sync.SyncAsync();

        // The rig and the cook are in the first batch, so no reading is ever
        // sent to a server that has not seen its parent.
        var first = _client.Pushes[0];

        Assert.Single(first.Equipment);
        Assert.Single(first.Cooks);
    }

    [Fact]
    public async Task A_device_a_month_behind_catches_up_in_one_sync()
    {
        var first = ARig("Page one");
        var second = ARig("Page two");

        _client.PullPages.Enqueue(new SyncPullResponse { Equipment = [first], Cursor = 1, HasMore = true });
        _client.PullPages.Enqueue(new SyncPullResponse { Equipment = [second], Cursor = 2, HasMore = false });

        var result = await _sync.SyncAsync();

        // Otherwise catching up on a month away would take a month of launches.
        Assert.Equal(2, result.Pulled);
        Assert.False(result.HasMore);
        Assert.NotNull(await _db.Equipment.GetAsync(second.Id));
    }

    [Fact]
    public async Task A_server_that_claims_more_but_does_not_advance_is_not_followed_forever()
    {
        // A cursor that never moves while the server keeps saying "more".
        _client.PullResponse = new SyncPullResponse { Cursor = 0, HasMore = true };

        var result = await _sync.SyncAsync();

        Assert.Single(_client.Pulls);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task A_wholly_rejected_batch_is_not_retried_in_the_same_round()
    {
        var rig = await ARigAsync();
        _client.Reject.Add(rig.Id);

        await _sync.SyncAsync();

        // Nothing was acknowledged, so collecting again would return the same
        // batch. Sending it again in a loop would never terminate.
        Assert.Single(_client.Pushes);
    }

    [Fact]
    public async Task Every_push_carries_this_devices_id()
    {
        await ARigAsync();

        await _sync.SyncAsync();

        Assert.Equal(_state.DeviceId, Assert.Single(_client.Pushes).DeviceId);
        Assert.Equal(_state.DeviceId, Assert.Single(_client.Pulls).Device);
    }

    [Fact]
    public async Task Two_rounds_at_once_do_not_both_run()
    {
        await ARigAsync();

        var gate = new SemaphoreSlim(0, 1);
        var blocking = new BlockingSyncClient(gate);
        var sync = new Humo.Core.Sync.SyncService(_db.Queue, blocking, _state, _db.Account, _db.Clock);

        var first = sync.SyncAsync();
        var second = await sync.SyncAsync();

        gate.Release();
        await first;

        // Overlapping rounds would race on the cursor and on SyncedAt. Dropping
        // the second is right: whatever it would have sent is still queued.
        Assert.Equal(SyncOutcome.AlreadyRunning, second.Outcome);
    }

    private async Task<Equipment> ARigAsync(string name = "Old Country Brazos")
    {
        var rig = ARig(name);
        await _db.Equipment.SaveAsync(rig);
        return rig;
    }

    private Equipment ARig(string name) => new()
    {
        Name = name,
        Type = EquipmentType.Offset,
        CreatedAt = _db.Clock.UtcNow,
        UpdatedAt = _db.Clock.UtcNow,
    };

    private async Task<Cook> ACookAsync(Guid equipmentId)
    {
        var cook = new Cook
        {
            EquipmentId = equipmentId,
            PitType = EquipmentType.Offset,
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            StartedAt = _db.Clock.UtcNow,
            LastActivityAt = _db.Clock.UtcNow,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow,
        };

        await _db.Cooks.SaveAsync(cook);
        return cook;
    }

    private TempEntry AReading(Guid cookId) => new()
    {
        CookId = cookId,
        RecordedAt = _db.Clock.UtcNow,
        MeatTempC = 68,
        CreatedAt = _db.Clock.UtcNow,
        UpdatedAt = _db.Clock.UtcNow,
    };

    /// <summary>Holds its push open until the test lets go.</summary>
    private sealed class BlockingSyncClient : ISyncClient
    {
        private readonly SemaphoreSlim _gate;

        public BlockingSyncClient(SemaphoreSlim gate) => _gate = gate;

        public async Task<SyncTransportResult<SyncPushResponse>> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);

            return SyncTransportResult<SyncPushResponse>.Ok(new SyncPushResponse
            {
                Accepted = request.Count,
            });
        }

        public Task<SyncTransportResult<SyncPullResponse>> PullAsync(
            Guid deviceId,
            long cursor,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SyncTransportResult<SyncPullResponse>.Ok(
                new SyncPullResponse { Cursor = cursor, HasMore = false }));
    }
}
