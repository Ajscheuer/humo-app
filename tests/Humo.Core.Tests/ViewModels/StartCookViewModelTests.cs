using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Enums;
using Humo.Shared.Units;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class StartCookViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private StartCookViewModel CreateViewModel()
        => new(_db.Service, _db.EquipmentService, _settings, _localizer, _navigation);

    /// <summary>
    /// The form as the user actually meets it: loaded, with a rig selected. A
    /// cook cannot start without one, so anything that starts a cook needs this.
    /// </summary>
    private async Task<StartCookViewModel> CreateLoadedViewModelAsync()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public void Every_meat_type_is_offered_with_a_key_that_resolves()
    {
        var vm = CreateViewModel();

        Assert.Equal(
            Enum.GetValues<MeatType>().Length,
            vm.MeatTypes.Count);

        // A key with no resource would show up in the picker as the raw key.
        foreach (var option in vm.MeatTypes)
        {
            Assert.NotEqual(option.DisplayNameKey, _localizer[option.DisplayNameKey]);
        }
    }

    [Fact]
    public async Task Weight_is_pre_filled_from_the_meat_type()
    {
        _settings.WeightUnit.Returns(WeightUnit.Kilograms);
        var vm = await CreateLoadedViewModelAsync();

        // Required, but never a blocker: the field arrives with a sensible number
        // so a cook who did not weigh the meat is not stopped at a blank box.
        Assert.Equal(6.0, vm.Weight);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public void The_pre_filled_weight_is_shown_in_the_users_unit()
    {
        _settings.WeightUnit.Returns(WeightUnit.Pounds);
        var vm = CreateViewModel();

        // 6 kg of brisket, read by someone who thinks in pounds.
        Assert.Equal(13.2, vm.Weight, precision: 1);
    }

    [Fact]
    public void Changing_meat_type_re_seeds_the_weight()
    {
        _settings.WeightUnit.Returns(WeightUnit.Kilograms);
        var vm = CreateViewModel();

        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.PorkRibs);

        // Picking ribs after brisket should not leave 6 kg sitting in the box.
        Assert.Equal(1.5, vm.Weight);
    }

    [Fact]
    public void Free_text_is_only_offered_for_Other()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsOtherMeatType);

        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.Other);
        Assert.True(vm.IsOtherMeatType);
    }

    [Fact]
    public void Leaving_Other_clears_the_free_text()
    {
        var vm = CreateViewModel();
        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.Other);
        vm.MeatTypeOther = "Goat";

        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.Chicken);

        // Otherwise a chicken cook would carry contradictory free text.
        Assert.Null(vm.MeatTypeOther);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public async Task A_cook_cannot_be_started_without_a_sensible_weight(double weight)
    {
        // Loaded, so a rig is selected and the weight is the only thing wrong.
        // Against an unloaded form this would pass with the weight guard deleted.
        var vm = await CreateLoadedViewModelAsync();
        vm.Weight = weight;

        Assert.False(vm.CanStart);
        Assert.False(vm.StartCookCommand.CanExecute(null));
    }

    [Fact]
    public void A_cook_cannot_be_started_before_a_rig_is_loaded()
    {
        var vm = CreateViewModel();

        // Every cook is attached to a rig, because the fire model learns per rig.
        Assert.Null(vm.SelectedEquipment);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task The_first_cook_creates_the_implicit_rig()
    {
        var vm = await CreateLoadedViewModelAsync();

        // A user who has never opened the equipment screen must still be able to
        // start a cook; the form mints the default rig rather than dead-ending
        // on "add equipment first".
        var rig = Assert.Single(vm.Equipment);
        Assert.Equal(rig, vm.SelectedEquipment);
        Assert.False(vm.HasEquipmentChoice);
    }

    [Fact]
    public async Task A_second_rig_turns_the_picker_on()
    {
        await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Kamado",
            Type = EquipmentType.Kamado,
        });
        await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Old Country Brazos",
            Type = EquipmentType.Offset,
        });

        var vm = await CreateLoadedViewModelAsync();

        // With one rig the answer is forced and the picker is a tap that buys
        // nothing; with two it is a real choice.
        Assert.Equal(2, vm.Equipment.Count);
        Assert.True(vm.HasEquipmentChoice);
    }

    [Fact]
    public async Task The_cook_is_attached_to_the_rig_that_was_chosen()
    {
        await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Kettle",
            Type = EquipmentType.Kettle,
        });
        var kamado = await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Kamado",
            Type = EquipmentType.Kamado,
        });

        var vm = await CreateLoadedViewModelAsync();
        vm.SelectedEquipment = vm.Equipment.First(e => e.Id == kamado.Id);

        await vm.StartCookCommand.ExecuteAsync(null);

        // And the pit type is snapshotted from the rig actually picked, not from
        // whichever one happened to be first in the list.
        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Equal(kamado.Id, cook.EquipmentId);
        Assert.Equal(EquipmentType.Kamado, cook.PitType);
    }

    [Fact]
    public async Task Weight_entered_in_pounds_is_stored_in_kilograms()
    {
        _settings.WeightUnit.Returns(WeightUnit.Pounds);
        var vm = await CreateLoadedViewModelAsync();
        vm.Weight = 15;

        await vm.StartCookCommand.ExecuteAsync(null);

        // Storage is kilograms, everywhere. A pound value reaching the database
        // would silently corrupt every time-per-kg figure derived from it.
        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Equal(6.80388555, cook.WeightKg, precision: 6);
    }

    [Fact]
    public async Task A_target_temperature_entered_in_Fahrenheit_is_stored_in_Celsius()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        var vm = await CreateLoadedViewModelAsync();
        vm.TargetTemperature = 203;

        await vm.StartCookCommand.ExecuteAsync(null);

        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Equal(95.0, cook.TargetInternalTempC!.Value, precision: 1);
    }

    [Fact]
    public async Task A_blank_target_temperature_stays_blank()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.TargetTemperature = null;

        await vm.StartCookCommand.ExecuteAsync(null);

        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Null(cook.TargetInternalTempC);
    }

    [Fact]
    public async Task Starting_a_cook_takes_the_user_to_the_cook_screen()
    {
        var vm = await CreateLoadedViewModelAsync();

        await vm.StartCookCommand.ExecuteAsync(null);

        // Leaving the user on the form they just submitted is how a cook ends up
        // starting the same brisket twice.
        await _navigation.Received(1).GoToAsync(AppRoutes.ActiveCook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Free_text_that_is_only_whitespace_is_not_stored()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.Other);
        vm.MeatTypeOther = "   ";

        await vm.StartCookCommand.ExecuteAsync(null);

        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Null(cook.MeatTypeOther);
    }

    [Fact]
    public async Task Free_text_is_trimmed_before_it_is_stored()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SelectedMeatType = vm.MeatTypes.First(o => o.Value == MeatType.Other);
        vm.MeatTypeOther = "  Goat  ";

        await vm.StartCookCommand.ExecuteAsync(null);

        // Otherwise " Goat" and "Goat " are two different things to every future
        // grouping or baseline that keys on this text.
        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.Equal("Goat", cook.MeatTypeOther);
    }

    [Fact]
    public async Task The_cook_records_the_pit_it_was_run_on()
    {
        var vm = await CreateLoadedViewModelAsync();

        await vm.StartCookCommand.ExecuteAsync(null);

        // A cook always records what it was run on. The pit type is a snapshot,
        // so editing or deleting the rig later cannot rewrite history.
        var cook = (await _db.Service.GetActiveCookAsync())!;
        Assert.NotEqual(Guid.Empty, cook.EquipmentId);
        Assert.Equal(EquipmentType.Offset, cook.PitType);
    }

    [Fact]
    public void Choosing_Spanish_does_not_change_the_units_shown()
    {
        _settings.TemperatureUnit.Returns(TemperatureUnit.Fahrenheit);
        _settings.WeightUnit.Returns(WeightUnit.Pounds);
        var vm = CreateViewModel();

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        // An American cook with a Spanish phone still reads °F and pounds.
        Assert.Equal("°F", vm.TemperatureUnitSymbol);
        Assert.Equal("lb", vm.WeightUnitSymbol);
        Assert.Equal("Empezar una cocción", vm.Title);
    }
}
