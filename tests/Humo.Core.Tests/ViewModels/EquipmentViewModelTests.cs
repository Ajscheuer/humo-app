using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Enums;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class EquipmentViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private EquipmentListViewModel CreateList()
        => new(_db.EquipmentService, _localizer, _navigation);

    private EquipmentEditViewModel CreateEdit()
        => new(_db.EquipmentService, _localizer, _navigation);

    private Task<Humo.Shared.Entities.Equipment> ARigAsync(
        string name = "Old Country Brazos",
        EquipmentType type = EquipmentType.Offset)
        => _db.EquipmentService.SaveAsync(new SaveEquipmentRequest { Name = name, Type = type });

    [Fact]
    public async Task An_empty_list_says_so_rather_than_showing_nothing()
    {
        var vm = CreateList();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Items);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public async Task Each_rig_is_listed_with_a_type_key_that_resolves()
    {
        await ARigAsync("Brazos", EquipmentType.Offset);
        await ARigAsync("Big Green Egg", EquipmentType.Kamado);

        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasItems);
        Assert.Equal(["Brazos", "Big Green Egg"], vm.Items.Select(i => i.Name));

        // The type is stored as an enum and displayed through a resource lookup,
        // which is what lets the same record read correctly in both languages.
        foreach (var item in vm.Items)
        {
            Assert.NotEqual(item.TypeKey, _localizer[item.TypeKey]);
        }
    }

    [Fact]
    public async Task Deleting_a_rig_removes_it_from_the_list()
    {
        var rig = await ARigAsync();
        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DeleteCommand.ExecuteAsync(vm.Items.Single());

        Assert.Empty(vm.Items);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Deleting_a_rig_with_a_cook_on_it_explains_itself()
    {
        var rig = await ARigAsync();
        await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });

        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Items.Single());

        // A normal thing to try. The app must say why, not fall over and not
        // appear to ignore the tap.
        Assert.Single(vm.Items);
        Assert.Equal(_localizer[AppStrings.Equipment_InUse], vm.ErrorMessage);
    }

    [Fact]
    public async Task Reloading_clears_a_stale_error()
    {
        var rig = await ARigAsync();
        var cook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });

        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Items.Single());
        Assert.NotNull(vm.ErrorMessage);

        await _db.Service.FinishCookAsync(cook.Id);
        await vm.LoadCommand.ExecuteAsync(null);

        // Otherwise "there is a cook running" stays on screen after it stops
        // being true.
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Editing_a_rig_navigates_with_its_id()
    {
        var rig = await ARigAsync();
        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.EditCommand.ExecuteAsync(vm.Items.Single());

        await _navigation.Received(1).GoToAsync(
            AppRoutes.EditEquipmentFor(rig.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_rig_needs_a_name_and_nothing_else()
    {
        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.CanSave);

        vm.Name = "Brazos";
        Assert.True(vm.CanSave);

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(await _db.EquipmentService.GetAllAsync());
        Assert.Equal("Brazos", saved.Name);
        Assert.Equal(EquipmentType.Offset, saved.Type);
        Assert.Equal(InsulationLevel.None, saved.Insulation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_of_nothing_but_spaces_does_not_enable_save(string name)
    {
        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Name = name;

        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public async Task A_volume_that_is_present_must_be_positive(double volume)
    {
        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Name = "Brazos";

        vm.FireboxVolumeL = volume;
        Assert.False(vm.CanSave);

        vm.FireboxVolumeL = null;
        Assert.True(vm.CanSave);

        vm.CookChamberVolumeL = volume;
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task Editing_loads_the_rig_as_it_stands()
    {
        var rig = await _db.EquipmentService.SaveAsync(new SaveEquipmentRequest
        {
            Name = "Brazos",
            Type = EquipmentType.Offset,
            Insulation = InsulationLevel.Heavy,
            FireboxVolumeL = 120,
            Notes = "Gasket added",
        });

        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(rig.Id);

        Assert.Equal("Brazos", vm.Name);
        Assert.Equal(EquipmentType.Offset, vm.SelectedType.Value);
        Assert.Equal(InsulationLevel.Heavy, vm.SelectedInsulation.Value);
        Assert.Equal(120, vm.FireboxVolumeL);
        Assert.Equal("Gasket added", vm.Notes);
    }

    [Fact]
    public async Task Saving_an_edit_updates_the_rig_rather_than_adding_one()
    {
        var rig = await ARigAsync("Brazos");

        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(rig.Id);
        vm.Name = "Brazos (rebuilt)";
        await vm.SaveCommand.ExecuteAsync(null);

        var all = await _db.EquipmentService.GetAllAsync();
        Assert.Equal(rig.Id, Assert.Single(all).Id);
        Assert.Equal("Brazos (rebuilt)", all[0].Name);
    }

    [Fact]
    public async Task Saving_returns_the_user_to_where_they_came_from()
    {
        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Name = "Brazos";

        await vm.SaveCommand.ExecuteAsync(null);

        await _navigation.Received(1).GoBackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_a_rig_deleted_underneath_falls_back_to_adding_one()
    {
        var rig = await ARigAsync();
        await _db.EquipmentService.DeleteAsync(rig.Id);

        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(rig.Id);
        vm.Name = "Replacement";
        await vm.SaveCommand.ExecuteAsync(null);

        // Deleted on another device between the list loading and this tap. The
        // form must not bind to nothing, and saving must not resurrect the old id.
        var saved = Assert.Single(await _db.EquipmentService.GetAllAsync());
        Assert.NotEqual(rig.Id, saved.Id);
        Assert.Equal("Replacement", saved.Name);
    }

    [Fact]
    public async Task Saving_a_rig_deleted_underneath_explains_itself_rather_than_crashing()
    {
        var rig = await ARigAsync();

        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(rig.Id);

        // Deleted between this form loading and Save being tapped.
        await _db.EquipmentService.DeleteAsync(rig.Id);
        vm.Name = "Brazos (rebuilt)";
        await vm.SaveCommand.ExecuteAsync(null);

        // LoadAsync already degrades to "add" for this race; the save path used
        // to crash on it instead.
        Assert.Equal(_localizer[AppStrings.Equipment_Gone], vm.ErrorMessage);
        Assert.Empty(await _db.EquipmentService.GetAllAsync());
        await _navigation.DidNotReceive().GoBackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rig_type_this_build_does_not_know_falls_back_instead_of_throwing()
    {
        // What a rig written by a newer version of the app looks like after it
        // syncs down: stored enum values with no option in this build.
        var rig = await ARigAsync();
        rig.Type = (EquipmentType)777;
        rig.Insulation = (InsulationLevel)888;
        await _db.Equipment.SaveAsync(rig);

        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(rig.Id);

        Assert.Equal(vm.EquipmentTypes[0], vm.SelectedType);
        Assert.Equal(vm.InsulationLevels[0], vm.SelectedInsulation);
    }

    [Fact]
    public async Task Every_type_and_insulation_level_is_offered_with_a_key_that_resolves()
    {
        var vm = CreateEdit();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(Enum.GetValues<EquipmentType>().Length, vm.EquipmentTypes.Count);
        Assert.Equal(Enum.GetValues<InsulationLevel>().Length, vm.InsulationLevels.Count);

        foreach (var key in vm.EquipmentTypes.Select(o => o.DisplayNameKey)
                     .Concat(vm.InsulationLevels.Select(o => o.DisplayNameKey)))
        {
            Assert.NotEqual(key, _localizer[key]);
        }
    }

    [Fact]
    public async Task The_title_says_whether_this_is_an_add_or_an_edit()
    {
        var rig = await ARigAsync();

        var adding = CreateEdit();
        await adding.LoadCommand.ExecuteAsync(null);
        Assert.Equal(_localizer[AppStrings.Equipment_Add], adding.Title);

        var editing = CreateEdit();
        await editing.LoadCommand.ExecuteAsync(rig.Id);
        Assert.Equal(_localizer[AppStrings.Equipment_Edit], editing.Title);
    }

    [Fact]
    public async Task The_list_reads_in_Spanish_without_touching_the_data()
    {
        await ARigAsync("Brazos", EquipmentType.Kamado);

        var vm = CreateList();
        await vm.LoadCommand.ExecuteAsync(null);
        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        // The rig's own name is user data and stays as typed; its type is an enum
        // and follows the language.
        Assert.Equal("Brazos", vm.Items.Single().Name);
        Assert.Equal("Mis equipos", vm.Title);
        Assert.Equal("Kamado", _localizer[vm.Items.Single().TypeKey]);
    }
}
