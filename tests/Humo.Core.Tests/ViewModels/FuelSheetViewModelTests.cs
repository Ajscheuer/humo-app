using Humo.Core.Localization;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class FuelSheetViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private FuelSheetViewModel CreateViewModel()
        => new(_db.FuelService, _settings, _localizer);

    private Task<Equipment> ARigAsync() => _db.Service.GetOrCreateDefaultEquipmentAsync();

    /// <summary>The sheet as it opens for a rig — tap one of the two-tap path.</summary>
    private async Task<FuelSheetViewModel> OpenedForAsync(Guid equipmentId, Guid? cookId = null)
    {
        var vm = CreateViewModel();
        await vm.PrepareCommand.ExecuteAsync(new FuelSheetContext(equipmentId, cookId));
        return vm;
    }

    [Fact]
    public async Task Logging_fuel_takes_two_taps()
    {
        var rig = await ARigAsync();

        // Tap 1: open the sheet. Everything but the size is already filled in.
        var vm = await OpenedForAsync(rig.Id);

        // Tap 2: a size class. This commits -- there is no third confirm tap.
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium);

        // The hard interaction requirement from product-spec.md 4.4, asserted
        // as a count of interactions rather than as a description of one.
        var logged = Assert.Single(await _db.FuelService.GetForEquipmentAsync(rig.Id));
        Assert.Equal(SizeClass.Medium, logged.SizeClass);
        Assert.Equal(1, logged.Count);
    }

    [Fact]
    public async Task The_first_load_on_a_new_rig_still_takes_two_taps()
    {
        var rig = await ARigAsync();

        var vm = await OpenedForAsync(rig.Id);

        // A rig with no history has nothing to pre-fill from, so the cold-start
        // fallback has to be a usable answer rather than an empty form -- being
        // wrong costs one correction, being blank costs the guarantee.
        Assert.False(vm.PrefilledFromLastLoad);
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Large);

        var logged = Assert.Single(await _db.FuelService.GetForEquipmentAsync(rig.Id));
        Assert.Equal(WoodType.Oak, logged.WoodType);
        Assert.Equal(FuelForm.Split, logged.Form);
    }

    [Fact]
    public async Task The_sheet_opens_with_the_last_load_on_this_rig()
    {
        var rig = await ARigAsync();
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = rig.Id,
            WoodType = WoodType.Hickory,
            Form = FuelForm.Chunk,
            SizeClass = SizeClass.Small,
        });

        var vm = await OpenedForAsync(rig.Id);

        // Cooks feed a fire the same wood all night. Remembering it is what
        // removes two of the four taps this would otherwise take.
        Assert.Equal(WoodType.Hickory, vm.SelectedWoodType.Value);
        Assert.Equal(FuelForm.Chunk, vm.SelectedForm.Value);
        Assert.True(vm.PrefilledFromLastLoad);
    }

    [Fact]
    public async Task The_pre_fill_does_not_leak_between_rigs()
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

        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = offset.Id,
            WoodType = WoodType.PostOak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Large,
        });

        var vm = await OpenedForAsync(kamado.Id);

        // Post oak splits in a kamado would be nonsense. Each fire has its own
        // history, which is the point of scoping fuel to the rig.
        Assert.False(vm.PrefilledFromLastLoad);
        Assert.Equal(WoodType.Oak, vm.SelectedWoodType.Value);
    }

    [Fact]
    public async Task The_size_and_count_are_not_carried_forward()
    {
        var rig = await ARigAsync();
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = rig.Id,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Large,
            Count = 3,
        });

        var vm = await OpenedForAsync(rig.Id);
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Small);

        // Size is the judgement the cook makes each time, and a remembered count
        // would silently log three splits when they added one.
        Assert.Equal(1, vm.Count);
        var events = await _db.FuelService.GetForEquipmentAsync(rig.Id);
        Assert.Equal(SizeClass.Small, events[^1].SizeClass);
        Assert.Equal(1, events[^1].Count);
    }

    [Fact]
    public async Task Fuel_belongs_to_the_fire_even_with_two_cooks_running()
    {
        var rig = await ARigAsync();
        var brisket = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });
        var ribs = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkRibs,
            WeightKg = 1.5,
            EquipmentId = rig.Id,
        });

        var vm = await OpenedForAsync(rig.Id, brisket.Id);
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium);

        // One split fed one fire. Logging it against each cook would make the
        // model see every load twice and predict roughly twice as often as the
        // fire actually needs.
        var logged = Assert.Single(await _db.FuelService.GetForEquipmentAsync(rig.Id));
        Assert.Equal(rig.Id, logged.EquipmentId);
        Assert.Equal(brisket.Id, logged.CookId);
        Assert.NotEqual(ribs.Id, logged.CookId);
    }

    [Fact]
    public async Task Weight_entered_in_pounds_is_stored_in_kilograms()
    {
        _settings.WeightUnit.Returns(WeightUnit.Pounds);
        var rig = await ARigAsync();

        var vm = await OpenedForAsync(rig.Id);
        vm.Weight = 10;
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium);

        var logged = Assert.Single(await _db.FuelService.GetForEquipmentAsync(rig.Id));
        Assert.Equal(4.5359237, logged.WeightKg!.Value, precision: 6);
    }

    [Fact]
    public async Task An_unweighed_load_stores_no_weight()
    {
        var rig = await ARigAsync();

        var vm = await OpenedForAsync(rig.Id);
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium);

        // Weight is never on the fast path, so absent is the normal case and
        // must not become zero.
        Assert.Null(Assert.Single(await _db.FuelService.GetForEquipmentAsync(rig.Id)).WeightKg);
    }

    [Fact]
    public async Task Free_text_is_only_kept_for_Other_wood()
    {
        var rig = await ARigAsync();
        var vm = await OpenedForAsync(rig.Id);

        vm.SelectedWoodType = vm.WoodTypes.First(o => o.Value == WoodType.Other);
        Assert.True(vm.IsOtherWoodType);
        vm.WoodTypeOther = "Olive";

        vm.SelectedWoodType = vm.WoodTypes.First(o => o.Value == WoodType.Cherry);

        // Otherwise a cherry load would carry contradictory free text.
        Assert.False(vm.IsOtherWoodType);
        Assert.Null(vm.WoodTypeOther);
    }

    [Fact]
    public async Task Other_wood_carries_its_free_text_into_the_next_sheet()
    {
        var rig = await ARigAsync();
        var vm = await OpenedForAsync(rig.Id);

        vm.SelectedWoodType = vm.WoodTypes.First(o => o.Value == WoodType.Other);
        vm.WoodTypeOther = "  Olive  ";
        await vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium);

        // Someone burning a wood Humo does not list should still get the two-tap
        // path on their second load.
        var next = await OpenedForAsync(rig.Id);
        Assert.Equal(WoodType.Other, next.SelectedWoodType.Value);
        Assert.Equal("Olive", next.WoodTypeOther);
    }

    [Fact]
    public async Task Every_wood_type_and_form_is_offered_with_a_key_that_resolves()
    {
        var vm = CreateViewModel();

        Assert.Equal(Enum.GetValues<WoodType>().Length, vm.WoodTypes.Count);
        Assert.Equal(Enum.GetValues<FuelForm>().Length, vm.FuelForms.Count);
        Assert.Equal(Enum.GetValues<SizeClass>().Length, vm.SizeClasses.Count);

        foreach (var key in vm.WoodTypes.Select(o => o.DisplayNameKey)
                     .Concat(vm.FuelForms.Select(o => o.DisplayNameKey))
                     .Concat(vm.SizeClasses.Select(o => o.DisplayNameKey)))
        {
            Assert.NotEqual(key, _localizer[key]);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void The_size_buttons_are_always_in_the_same_order()
    {
        var vm = CreateViewModel();

        // The second tap of the fast path is muscle memory in the dark. If these
        // ever reorder, a cook logs "large" when they meant "small".
        Assert.Equal(
            [SizeClass.Small, SizeClass.Medium, SizeClass.Large],
            vm.SizeClasses.Select(o => o.Value));
    }

    [Fact]
    public async Task Logging_without_a_rig_is_refused_rather_than_writing_nowhere()
    {
        var vm = CreateViewModel();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.LogSizeCommand.ExecuteAsync(SizeClass.Medium));
    }

    [Fact]
    public async Task Choosing_Spanish_does_not_change_the_weight_unit()
    {
        _settings.WeightUnit.Returns(WeightUnit.Pounds);
        var rig = await ARigAsync();
        var vm = await OpenedForAsync(rig.Id);

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        Assert.Equal("lb", vm.WeightUnitSymbol);
        Assert.Equal("Combustible", vm.Title);
    }
}
