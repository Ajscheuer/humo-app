using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Services;

public class FuelServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private IFuelService Service => _db.FuelService;

    private Task<Equipment> ARigAsync() => _db.Service.GetOrCreateDefaultEquipmentAsync();

    private static LogFuelRequest ASplit(Guid equipmentId) => new()
    {
        EquipmentId = equipmentId,
        WoodType = WoodType.Oak,
        Form = FuelForm.Split,
        SizeClass = SizeClass.Medium,
    };

    [Fact]
    public async Task A_fuel_event_records_when_it_happened_and_what_fed_the_fire()
    {
        var rig = await ARigAsync();

        var logged = await Service.LogFuelAsync(ASplit(rig.Id));

        Assert.NotEqual(Guid.Empty, logged.Id);
        Assert.Equal(rig.Id, logged.EquipmentId);
        Assert.Equal(_db.Clock.UtcNow, logged.RecordedAt);
        Assert.Equal(1, logged.Count);
        Assert.False(logged.ViaNotification);
    }

    [Fact]
    public async Task A_fuel_event_against_a_rig_that_does_not_exist_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.LogFuelAsync(ASplit(Guid.NewGuid())));
    }

    [Fact]
    public async Task The_cold_start_default_is_usable_rather_than_empty()
    {
        var rig = await ARigAsync();

        var defaults = await Service.GetDefaultsAsync(rig.Id);

        // Being wrong here costs one correction; being blank costs the two-tap
        // guarantee outright.
        Assert.Equal(WoodType.Oak, defaults.WoodType);
        Assert.Equal(FuelForm.Split, defaults.Form);
        Assert.False(defaults.FromPreviousEvent);
        Assert.Null(defaults.WoodTypeOther);
    }

    [Fact]
    public async Task The_defaults_come_from_the_most_recent_load_not_the_first()
    {
        var rig = await ARigAsync();

        await Service.LogFuelAsync(ASplit(rig.Id) with { WoodType = WoodType.Mesquite });
        _db.Clock.Advance(TimeSpan.FromHours(1));
        await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Hickory,
            Form = FuelForm.Chunk,
        });

        var defaults = await Service.GetDefaultsAsync(rig.Id);

        Assert.Equal(WoodType.Hickory, defaults.WoodType);
        Assert.Equal(FuelForm.Chunk, defaults.Form);
        Assert.True(defaults.FromPreviousEvent);
    }

    [Fact]
    public async Task Most_recent_means_when_it_was_burned_not_when_it_was_typed()
    {
        var rig = await ARigAsync();
        _db.Clock.Advance(TimeSpan.FromHours(4));

        await Service.LogFuelAsync(ASplit(rig.Id) with { WoodType = WoodType.Hickory });

        // Catching up on a load from two hours ago, entered after the newer one.
        await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Mesquite,
            RecordedAt = _db.Clock.UtcNow - TimeSpan.FromHours(2),
        });

        // The sheet should still open on hickory: the fire is burning hickory.
        var defaults = await Service.GetDefaultsAsync(rig.Id);
        Assert.Equal(WoodType.Hickory, defaults.WoodType);
    }

    [Fact]
    public async Task Free_text_is_only_kept_when_the_wood_is_Other()
    {
        var rig = await ARigAsync();

        var logged = await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Oak,
            WoodTypeOther = "Olive",
        });

        // Oak with "Olive" attached would be two contradictory answers on one row.
        Assert.Null(logged.WoodTypeOther);
    }

    [Fact]
    public async Task Free_text_is_trimmed_and_blank_text_is_dropped()
    {
        var rig = await ARigAsync();

        var trimmed = await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Other,
            WoodTypeOther = "  Olive  ",
        });
        var blank = await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Other,
            WoodTypeOther = "   ",
        });

        Assert.Equal("Olive", trimmed.WoodTypeOther);
        Assert.Null(blank.WoodTypeOther);
    }

    [Fact]
    public async Task The_free_text_carries_into_the_defaults()
    {
        var rig = await ARigAsync();
        await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            WoodType = WoodType.Other,
            WoodTypeOther = "Olive",
        });

        var defaults = await Service.GetDefaultsAsync(rig.Id);

        Assert.Equal(WoodType.Other, defaults.WoodType);
        Assert.Equal("Olive", defaults.WoodTypeOther);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_fuel_event_records_at_least_one_piece(int count)
    {
        var rig = await ARigAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service.LogFuelAsync(ASplit(rig.Id) with { Count = count }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_weight_that_is_present_must_be_a_positive_number(double weight)
    {
        var rig = await ARigAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service.LogFuelAsync(ASplit(rig.Id) with { WeightKg = weight }));
    }

    [Fact]
    public async Task Fuel_history_is_scoped_to_one_rig()
    {
        var offset = await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Offset",
            Type = EquipmentType.Offset,
        });
        var kamado = await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Kamado",
            Type = EquipmentType.Kamado,
        });

        await Service.LogFuelAsync(ASplit(offset.Id));
        await Service.LogFuelAsync(ASplit(offset.Id));
        await Service.LogFuelAsync(ASplit(kamado.Id));

        Assert.Equal(2, (await Service.GetForEquipmentAsync(offset.Id)).Count);
        Assert.Single(await Service.GetForEquipmentAsync(kamado.Id));
    }

    [Fact]
    public async Task Fuel_history_is_oldest_first_even_when_entered_out_of_order()
    {
        var rig = await ARigAsync();
        _db.Clock.Advance(TimeSpan.FromHours(3));

        await Service.LogFuelAsync(ASplit(rig.Id) with { SizeClass = SizeClass.Large });
        await Service.LogFuelAsync(ASplit(rig.Id) with
        {
            SizeClass = SizeClass.Small,
            RecordedAt = _db.Clock.UtcNow - TimeSpan.FromHours(2),
        });

        // The fire model reads this as a series of intervals, so the order has to
        // be the fire's order, not the typing order.
        Assert.Equal(
            [SizeClass.Small, SizeClass.Large],
            (await Service.GetForEquipmentAsync(rig.Id)).Select(f => f.SizeClass));
    }

    [Fact]
    public async Task An_event_from_a_notification_is_marked_as_such()
    {
        var rig = await ARigAsync();

        var logged = await Service.LogFuelAsync(ASplit(rig.Id) with { ViaNotification = true });

        // An event created because we asked is not evidence of what the fire
        // needed; Level 1 excludes these from cadence learning entirely.
        Assert.True(logged.ViaNotification);
    }

    [Fact]
    public async Task The_cook_on_screen_is_recorded_but_the_fire_owns_the_event()
    {
        var rig = await ARigAsync();
        var cook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });

        var logged = await Service.LogFuelAsync(ASplit(rig.Id) with { CookId = cook.Id });

        Assert.Equal(cook.Id, logged.CookId);
        Assert.Equal(rig.Id, logged.EquipmentId);
    }

    [Fact]
    public async Task Fuel_can_be_logged_with_no_cook_running_at_all()
    {
        var rig = await ARigAsync();

        // Getting the fire up to temperature happens before any meat goes on.
        var logged = await Service.LogFuelAsync(ASplit(rig.Id));

        Assert.Null(logged.CookId);
    }

    [Fact]
    public async Task Every_field_round_trips_through_storage()
    {
        var rig = await ARigAsync();

        await Service.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = rig.Id,
            WoodType = WoodType.Quebracho,
            Form = FuelForm.Charcoal,
            SizeClass = SizeClass.Large,
            Count = 4,
            WeightKg = 2.75,
            ViaNotification = true,
        });

        var loaded = Assert.Single(await Service.GetForEquipmentAsync(rig.Id));
        Assert.Equal(WoodType.Quebracho, loaded.WoodType);
        Assert.Equal(FuelForm.Charcoal, loaded.Form);
        Assert.Equal(SizeClass.Large, loaded.SizeClass);
        Assert.Equal(4, loaded.Count);
        Assert.Equal(2.75, loaded.WeightKg);
        Assert.True(loaded.ViaNotification);
    }
}
