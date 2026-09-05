using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Services;

public class EquipmentServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private IEquipmentService Service => _db.EquipmentService;

    private static SaveEquipmentRequest ARig(string name = "Old Country Brazos") => new()
    {
        Name = name,
        Type = EquipmentType.Offset,
    };

    [Fact]
    public async Task A_new_rig_gets_an_id_and_timestamps()
    {
        var rig = await Service.SaveAsync(ARig());

        // Client-generated GUID: the record has its final identity before it has
        // ever seen a server.
        Assert.NotEqual(Guid.Empty, rig.Id);
        Assert.Equal(_db.Clock.UtcNow, rig.CreatedAt);
        Assert.Equal(_db.Clock.UtcNow, rig.UpdatedAt);
        Assert.Null(rig.DeletedAt);
    }

    [Fact]
    public async Task A_rig_round_trips_through_storage()
    {
        var saved = await Service.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Old Country Brazos",
            Type = EquipmentType.Offset,
            Insulation = InsulationLevel.Heavy,
            FireboxVolumeL = 120.5,
            CookChamberVolumeL = 400,
            Notes = "Gasket added",
        });

        var loaded = await Service.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Old Country Brazos", loaded.Name);
        Assert.Equal(EquipmentType.Offset, loaded.Type);
        Assert.Equal(InsulationLevel.Heavy, loaded.Insulation);
        Assert.Equal(120.5, loaded.FireboxVolumeL);
        Assert.Equal(400, loaded.CookChamberVolumeL);
        Assert.Equal("Gasket added", loaded.Notes);
    }

    [Fact]
    public async Task Editing_a_rig_keeps_its_id()
    {
        var rig = await Service.SaveAsync(ARig());
        _db.Clock.Advance(TimeSpan.FromDays(30));

        var edited = await Service.SaveAsync(new SaveEquipmentRequest
        {
            Id = rig.Id,
            Name = "Brazos (rebuilt)",
            Type = EquipmentType.Offset,
        });

        // The id is what every cook, fuel event and pit reading hangs off. An
        // edit that minted a new one would orphan the rig's whole history.
        Assert.Equal(rig.Id, edited.Id);
        Assert.Equal("Brazos (rebuilt)", edited.Name);
        Assert.Equal(rig.CreatedAt, edited.CreatedAt);
        Assert.Equal(_db.Clock.UtcNow, edited.UpdatedAt);
        Assert.Single(await Service.GetAllAsync());
    }

    [Fact]
    public async Task Editing_a_rig_that_no_longer_exists_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.SaveAsync(ARig() with { Id = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_rig_cannot_be_saved_without_a_name(string name)
    {
        // The name is how the user tells two smokers apart in every picker and
        // every chart from here on.
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service.SaveAsync(ARig() with { Name = name }));
    }

    [Fact]
    public async Task The_name_is_trimmed()
    {
        var rig = await Service.SaveAsync(ARig() with { Name = "  Brazos  " });

        Assert.Equal("Brazos", rig.Name);
    }

    [Fact]
    public async Task A_blank_note_is_stored_as_no_note()
    {
        var rig = await Service.SaveAsync(ARig() with { Notes = "   " });

        Assert.Null(rig.Notes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_volume_that_is_present_must_be_a_positive_number(double volume)
    {
        // The fire model treats absent and zero differently, so a zero-litre
        // firebox is worse than no answer at all.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service.SaveAsync(ARig() with { FireboxVolumeL = volume }));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service.SaveAsync(ARig() with { CookChamberVolumeL = volume }));
    }

    [Fact]
    public async Task Volumes_may_be_left_out_entirely()
    {
        var rig = await Service.SaveAsync(ARig());

        Assert.Null(rig.FireboxVolumeL);
        Assert.Null(rig.CookChamberVolumeL);
    }

    [Fact]
    public async Task Rigs_are_listed_oldest_first()
    {
        await Service.SaveAsync(ARig("First"));
        _db.Clock.Advance(TimeSpan.FromDays(1));
        await Service.SaveAsync(ARig("Second"));

        Assert.Equal(["First", "Second"], (await Service.GetAllAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task A_deleted_rig_disappears_from_the_list()
    {
        var rig = await Service.SaveAsync(ARig());

        await Service.DeleteAsync(rig.Id);

        Assert.Empty(await Service.GetAllAsync());
        Assert.Null(await Service.GetAsync(rig.Id));
    }

    [Fact]
    public async Task Deleting_a_rig_with_a_cook_running_on_it_is_refused()
    {
        var rig = await Service.SaveAsync(ARig());
        await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });

        // Deleting mid-cook would strand the readings and fuel events hanging
        // off this id, and the running cook would point at nothing.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.DeleteAsync(rig.Id));
        Assert.Single(await Service.GetAllAsync());
    }

    [Fact]
    public async Task A_rig_can_be_deleted_once_its_cook_is_finished()
    {
        var rig = await Service.SaveAsync(ARig());
        var cook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });

        await _db.Service.FinishCookAsync(cook.Id);
        await Service.DeleteAsync(rig.Id);

        Assert.Empty(await Service.GetAllAsync());
    }

    [Fact]
    public async Task A_cook_running_on_another_rig_does_not_block_this_one()
    {
        var offset = await Service.SaveAsync(ARig("Offset"));
        var kamado = await Service.SaveAsync(ARig("Kamado") with { Type = EquipmentType.Kamado });

        await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = offset.Id,
        });

        await Service.DeleteAsync(kamado.Id);

        Assert.Equal(["Offset"], (await Service.GetAllAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task Deleting_a_rig_keeps_the_history_of_the_cooks_that_ran_on_it()
    {
        var rig = await Service.SaveAsync(ARig());
        var cook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });
        await _db.Service.FinishCookAsync(cook.Id);

        await Service.DeleteAsync(rig.Id);

        // Soft delete on purpose. The finished cook still records what it ran on,
        // and its pit type was snapshotted at the start, so its history reads
        // correctly even though the rig is gone from the picker.
        var stored = await _db.Cooks.GetAsync(cook.Id);
        Assert.NotNull(stored);
        Assert.Equal(rig.Id, stored.EquipmentId);
        Assert.Equal(EquipmentType.Offset, stored.PitType);
    }

    [Fact]
    public async Task Deleting_a_rig_that_does_not_exist_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_the_same_rig_twice_is_refused_the_second_time()
    {
        var rig = await Service.SaveAsync(ARig());
        await Service.DeleteAsync(rig.Id);

        // Two taps on a stale list. The second must not silently "succeed" and
        // rewrite the deletion timestamp.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.DeleteAsync(rig.Id));
    }
}
