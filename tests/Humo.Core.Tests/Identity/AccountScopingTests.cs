using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Identity;

/// <summary>
/// One account's data never leaks into another's.
/// <para>
/// This is enforced in the repositories rather than in each service, so these
/// tests exercise the boundary that actually holds the line: reads filter by the
/// current account, and writes stamp it, whatever the caller does.
/// </para>
/// </summary>
public class AccountScopingTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private void SwitchToAnotherAccount()
        => _db.Account.SetCurrent(Guid.NewGuid(), isAnonymous: false);

    private Task<Humo.Shared.Entities.Cook> ACookAsync()
        => _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
        });

    [Fact]
    public async Task A_new_record_is_stamped_with_the_current_account()
    {
        var cook = await ACookAsync();

        var stored = await _db.Cooks.GetAsync(cook.Id);
        Assert.Equal(_db.Account.CurrentAccountId, stored!.AccountId);
    }

    [Fact]
    public async Task Another_account_cannot_read_this_accounts_cook()
    {
        var cook = await ACookAsync();

        SwitchToAnotherAccount();

        Assert.Null(await _db.Cooks.GetAsync(cook.Id));
        Assert.Empty(await _db.Cooks.GetUnfinishedAsync());
    }

    [Fact]
    public async Task Another_account_sees_an_empty_history()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        SwitchToAnotherAccount();

        Assert.Empty(await _db.Cooks.GetFinishedAsync());
    }

    [Fact]
    public async Task Another_account_sees_no_equipment()
    {
        await _db.Service.GetOrCreateDefaultEquipmentAsync();
        Assert.Single(await _db.Equipment.GetAllAsync());

        SwitchToAnotherAccount();

        Assert.Empty(await _db.Equipment.GetAllAsync());
    }

    [Fact]
    public async Task Another_account_sees_no_readings_milestones_or_fuel()
    {
        var cook = await ACookAsync();
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 68,
            PitTempC = 130,
        });
        await _db.Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });
        await _db.FuelService.LogFuelAsync(new LogFuelRequest
        {
            EquipmentId = cook.EquipmentId,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Medium,
        });

        SwitchToAnotherAccount();

        Assert.Empty(await _db.TempEntries.GetForCookAsync(cook.Id));
        Assert.Empty(await _db.Events.GetForCookAsync(cook.Id));
        Assert.Empty(await _db.FuelEvents.GetForEquipmentAsync(cook.EquipmentId));
        Assert.Null(await _db.FuelEvents.GetMostRecentForEquipmentAsync(cook.EquipmentId));
        Assert.Empty(await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Switching_back_finds_the_data_again()
    {
        var cook = await ACookAsync();
        var original = _db.Account.CurrentAccountId;

        SwitchToAnotherAccount();
        Assert.Null(await _db.Cooks.GetAsync(cook.Id));

        // Scoping hides, it does not delete. Signing out and back in must not
        // cost a user their cooks.
        _db.Account.SetCurrent(original, isAnonymous: true);
        Assert.NotNull(await _db.Cooks.GetAsync(cook.Id));
    }

    [Fact]
    public async Task Two_accounts_each_get_their_own_default_rig()
    {
        var first = await _db.Service.GetOrCreateDefaultEquipmentAsync();

        SwitchToAnotherAccount();
        var second = await _db.Service.GetOrCreateDefaultEquipmentAsync();

        // The second account must not silently adopt the first's smoker -- and
        // must not be told it already has one when it cannot see it.
        Assert.NotEqual(first.Id, second.Id);
        Assert.Single(await _db.Equipment.GetAllAsync());
    }

    [Fact]
    public async Task A_cook_started_under_one_account_stays_with_it()
    {
        var mine = await ACookAsync();
        var myAccount = _db.Account.CurrentAccountId;

        SwitchToAnotherAccount();
        var theirs = await ACookAsync();

        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.Equal(_db.Account.CurrentAccountId, (await _db.Cooks.GetAsync(theirs.Id))!.AccountId);

        _db.Account.SetCurrent(myAccount, isAnonymous: true);
        Assert.Equal(myAccount, (await _db.Cooks.GetAsync(mine.Id))!.AccountId);
        Assert.Null(await _db.Cooks.GetAsync(theirs.Id));
    }

    [Fact]
    public async Task A_record_saved_with_someone_elses_stamp_is_corrected_on_write()
    {
        var cook = await ACookAsync();
        cook.AccountId = Guid.NewGuid();

        // The repository is the authority, not the entity handed to it. A caller
        // that sets the wrong owner -- or none -- cannot write into another
        // account's data.
        await _db.Cooks.SaveAsync(cook);

        Assert.Equal(_db.Account.CurrentAccountId, (await _db.Cooks.GetAsync(cook.Id))!.AccountId);
    }
}
