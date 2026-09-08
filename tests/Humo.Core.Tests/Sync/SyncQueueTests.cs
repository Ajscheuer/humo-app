using Humo.Core.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;

namespace Humo.Core.Tests.Sync;

public class SyncQueueTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly Guid _device = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_new_record_is_queued()
    {
        var rig = await ARigAsync();

        var batch = await _db.Queue.CollectAsync(_device);

        Assert.Equal(rig.Id, Assert.Single(batch.Equipment).Id);
    }

    [Fact]
    public async Task An_empty_database_queues_nothing()
    {
        var batch = await _db.Queue.CollectAsync(_device);

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public async Task An_acknowledged_record_is_not_queued_again()
    {
        await ARigAsync();

        var collectedAt = _db.Clock.UtcNow;
        var batch = await _db.Queue.CollectAsync(_device);
        await _db.Queue.AcknowledgeAsync(batch, [], collectedAt);

        Assert.Equal(0, (await _db.Queue.CollectAsync(_device)).Count);
    }

    [Fact]
    public async Task A_record_edited_after_being_synced_is_queued_again()
    {
        var rig = await ARigAsync();

        var collectedAt = _db.Clock.UtcNow;
        await _db.Queue.AcknowledgeAsync(await _db.Queue.CollectAsync(_device), [], collectedAt);

        _db.Clock.Advance(TimeSpan.FromMinutes(5));
        rig.Name = "Renamed";
        rig.UpdatedAt = _db.Clock.UtcNow;
        await _db.Equipment.SaveAsync(rig);

        Assert.Equal(rig.Id, Assert.Single((await _db.Queue.CollectAsync(_device)).Equipment).Id);
    }

    [Fact]
    public async Task A_rejected_record_stays_queued()
    {
        var rig = await ARigAsync();

        var collectedAt = _db.Clock.UtcNow;
        var batch = await _db.Queue.CollectAsync(_device);
        await _db.Queue.AcknowledgeAsync(batch, [rig.Id], collectedAt);

        // The server asked for it again with its parent. Marking it synced would
        // mean it never went up at all.
        Assert.Equal(rig.Id, Assert.Single((await _db.Queue.CollectAsync(_device)).Equipment).Id);
    }

    [Fact]
    public async Task An_edit_made_while_the_push_was_in_flight_stays_queued()
    {
        var rig = await ARigAsync();

        var collectedAt = _db.Clock.UtcNow;
        var batch = await _db.Queue.CollectAsync(_device);

        // The user renames the rig while the request is on the wire. The server
        // is acknowledging the version it received, not this one.
        _db.Clock.Advance(TimeSpan.FromSeconds(2));
        rig.Name = "Renamed mid-flight";
        rig.UpdatedAt = _db.Clock.UtcNow;
        await _db.Equipment.SaveAsync(rig);

        await _db.Queue.AcknowledgeAsync(batch, [], collectedAt);

        // Stamping the acknowledgement with "now" instead of the collection time
        // would mark this edit synced and lose it without a trace.
        Assert.Equal(rig.Id, Assert.Single((await _db.Queue.CollectAsync(_device)).Equipment).Id);
    }

    [Fact]
    public async Task A_queued_batch_carries_parents_before_children()
    {
        var rig = await ARigAsync();
        var cook = await ACookAsync(rig.Id);
        await _db.TempEntries.SaveAsync(AReading(cook.Id));

        var batch = await _db.Queue.CollectAsync(_device);

        var order = batch.InApplyOrder().Select(e => e.GetType().Name).ToList();

        Assert.Equal(["Equipment", "Cook", "TempEntry"], order);
    }

    [Fact]
    public async Task Acknowledging_nothing_leaves_everything_queued()
    {
        await ARigAsync();

        var batch = await _db.Queue.CollectAsync(_device);
        await _db.Queue.AcknowledgeAsync(
            new SyncPushRequest { DeviceId = _device },
            [],
            _db.Clock.UtcNow);

        Assert.Equal(batch.Count, (await _db.Queue.CollectAsync(_device)).Count);
    }

    [Fact]
    public async Task A_pulled_record_lands_in_the_database()
    {
        var incoming = new Equipment
        {
            Name = "From the other phone",
            Type = EquipmentType.Kamado,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow,
        };

        var applied = await _db.Queue.ApplyAsync(Pulled(equipment: [incoming]), _db.Clock.UtcNow);

        Assert.Equal(1, applied);
        Assert.Equal("From the other phone", (await _db.Equipment.GetAsync(incoming.Id))?.Name);
    }

    [Fact]
    public async Task A_pulled_record_is_not_immediately_queued_back_up()
    {
        var incoming = new Equipment
        {
            Name = "From the other phone",
            Type = EquipmentType.Kamado,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow,
        };

        await _db.Queue.ApplyAsync(Pulled(equipment: [incoming]), _db.Clock.UtcNow);

        // Otherwise every pull would create a push, and two devices would trade
        // the same record back and forth forever.
        Assert.Equal(0, (await _db.Queue.CollectAsync(_device)).Count);
    }

    [Fact]
    public async Task A_record_written_on_a_device_with_a_fast_clock_is_not_pushed_straight_back()
    {
        var incoming = new Equipment
        {
            Name = "Clock is ahead",
            Type = EquipmentType.Kamado,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow.AddDays(2),
        };

        await _db.Queue.ApplyAsync(Pulled(equipment: [incoming]), _db.Clock.UtcNow);

        Assert.Equal(0, (await _db.Queue.CollectAsync(_device)).Count);
    }

    [Fact]
    public async Task A_pulled_edit_overwrites_an_older_local_version()
    {
        var rig = await ARigAsync("Local name");

        var incoming = new Equipment
        {
            Id = rig.Id,
            Name = "Edited elsewhere",
            Type = rig.Type,
            CreatedAt = rig.CreatedAt,
            UpdatedAt = rig.UpdatedAt.AddHours(1),
        };

        await _db.Queue.ApplyAsync(Pulled(equipment: [incoming]), _db.Clock.UtcNow);

        Assert.Equal("Edited elsewhere", (await _db.Equipment.GetAsync(rig.Id))?.Name);
    }

    [Fact]
    public async Task A_pulled_edit_does_not_overwrite_a_newer_local_version()
    {
        var rig = await ARigAsync("Local name");

        var stale = new Equipment
        {
            Id = rig.Id,
            Name = "Older edit arriving late",
            Type = rig.Type,
            CreatedAt = rig.CreatedAt,
            UpdatedAt = rig.UpdatedAt.AddHours(-1),
        };

        var applied = await _db.Queue.ApplyAsync(Pulled(equipment: [stale]), _db.Clock.UtcNow);

        // The same last-write-wins rule the server applies, so both sides land
        // on the same version instead of trading edits.
        Assert.Equal(0, applied);
        Assert.Equal("Local name", (await _db.Equipment.GetAsync(rig.Id))?.Name);
    }

    [Fact]
    public async Task A_pulled_record_with_an_equal_timestamp_changes_nothing()
    {
        var rig = await ARigAsync("Local name");

        var same = new Equipment
        {
            Id = rig.Id,
            Name = "Should not win",
            Type = rig.Type,
            CreatedAt = rig.CreatedAt,
            UpdatedAt = rig.UpdatedAt,
        };

        Assert.Equal(0, await _db.Queue.ApplyAsync(Pulled(equipment: [same]), _db.Clock.UtcNow));
        Assert.Equal("Local name", (await _db.Equipment.GetAsync(rig.Id))?.Name);
    }

    [Fact]
    public async Task A_pulled_tombstone_hides_the_record()
    {
        var rig = await ARigAsync();

        var deleted = new Equipment
        {
            Id = rig.Id,
            Name = rig.Name,
            Type = rig.Type,
            CreatedAt = rig.CreatedAt,
            UpdatedAt = rig.UpdatedAt.AddHours(1),
            DeletedAt = rig.UpdatedAt.AddHours(1),
        };

        await _db.Queue.ApplyAsync(Pulled(equipment: [deleted]), _db.Clock.UtcNow);

        Assert.Null(await _db.Equipment.GetAsync(rig.Id));
    }

    [Fact]
    public async Task A_pull_never_overwrites_another_accounts_row()
    {
        var rig = await ARigAsync("Belongs to the first account");
        var firstAccount = _db.Account.CurrentAccountId;

        // Somebody else signs in on this phone.
        _db.Account.SetCurrent(Guid.NewGuid(), isAnonymous: false);

        var collision = new Equipment
        {
            Id = rig.Id,
            Name = "Different account, same id",
            Type = EquipmentType.Kamado,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow.AddHours(1),
        };

        var applied = await _db.Queue.ApplyAsync(Pulled(equipment: [collision]), _db.Clock.UtcNow);

        Assert.Equal(0, applied);

        _db.Account.SetCurrent(firstAccount, isAnonymous: true);
        Assert.Equal("Belongs to the first account", (await _db.Equipment.GetAsync(rig.Id))?.Name);
    }

    [Fact]
    public async Task Another_accounts_records_are_never_queued()
    {
        await ARigAsync();

        _db.Account.SetCurrent(Guid.NewGuid(), isAnonymous: false);

        Assert.Equal(0, (await _db.Queue.CollectAsync(_device)).Count);
    }

    private async Task<Equipment> ARigAsync(string name = "Old Country Brazos")
    {
        var rig = new Equipment
        {
            Name = name,
            Type = EquipmentType.Offset,
            CreatedAt = _db.Clock.UtcNow,
            UpdatedAt = _db.Clock.UtcNow,
        };

        await _db.Equipment.SaveAsync(rig);
        return rig;
    }

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

    private static SyncPullResponse Pulled(IReadOnlyList<Equipment>? equipment = null)
        => new() { Equipment = equipment ?? [], Cursor = 1, HasMore = false };
}
