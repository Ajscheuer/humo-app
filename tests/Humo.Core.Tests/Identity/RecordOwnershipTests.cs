using Humo.Core.Data;
using Humo.Core.Identity;
using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Identity;

/// <summary>
/// Records written before account scoping existed carry <see cref="Guid.Empty"/>.
/// Switching scoping on without adopting them would make every cook a user had
/// logged invisible at once — and for a guest with no account and no sync, that
/// is not "hidden", it is gone.
/// </summary>
public class RecordOwnershipTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>
    /// Reproduces what a build with no account scoping left behind: a full cook
    /// across every table, with every account id blanked afterwards. Blanked by
    /// direct SQL because the repositories now stamp ownership on write and can
    /// no longer produce this shape.
    /// </summary>
    private async Task<Guid> AnOwnerlessCookAsync()
    {
        var cook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
        });

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

        await BlankEveryAccountIdAsync();
        return cook.Id;
    }

    private async Task BlankEveryAccountIdAsync()
    {
        var db = await _db.Connection.GetConnectionAsync();
        foreach (var table in new[]
                 {
                     "equipment", "cooks", "temp_entries",
                     "pit_temp_entries", "fuel_events", "events",
                 })
        {
            await db.ExecuteAsync($"UPDATE {table} SET AccountId = ?", Guid.Empty);
        }
    }

    [Fact]
    public async Task Pre_account_records_are_invisible_until_they_are_claimed()
    {
        var cookId = await AnOwnerlessCookAsync();

        // This is the failure the claim exists to prevent, asserted directly.
        Assert.Null(await _db.Cooks.GetAsync(cookId));
        Assert.Empty(await _db.Equipment.GetAllAsync());
    }

    [Fact]
    public async Task Claiming_adopts_every_table()
    {
        var cookId = await AnOwnerlessCookAsync();
        var account = _db.Account.CurrentAccountId;

        var claimed = await _db.Ownership.ClaimUnownedRecordsAsync(account);

        // One rig, one cook, one reading, one pit reading, one milestone, one
        // fuel load. A table missed in the claim list loses its rows silently,
        // which is why the count is asserted rather than just "something moved".
        Assert.Equal(6, claimed);

        var cook = await _db.Cooks.GetAsync(cookId);
        Assert.NotNull(cook);
        Assert.Equal(account, cook.AccountId);
        Assert.Single(await _db.Equipment.GetAllAsync());
        Assert.Single(await _db.TempEntries.GetForCookAsync(cookId));
        Assert.Single(await _db.Events.GetForCookAsync(cookId));
        Assert.Single(await _db.FuelEvents.GetForEquipmentAsync(cook.EquipmentId));
        Assert.Single(await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Claiming_twice_adopts_nothing_the_second_time()
    {
        await AnOwnerlessCookAsync();
        var account = _db.Account.CurrentAccountId;

        Assert.Equal(6, await _db.Ownership.ClaimUnownedRecordsAsync(account));

        // It runs on every launch, so being a no-op once there is nothing left
        // to adopt is the normal case, not an edge one.
        Assert.Equal(0, await _db.Ownership.ClaimUnownedRecordsAsync(account));
    }

    [Fact]
    public async Task Claiming_leaves_another_accounts_records_alone()
    {
        // A cook that already belongs to someone.
        var owned = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkRibs,
            WeightKg = 1.5,
        });
        var owner = _db.Account.CurrentAccountId;

        var claimer = Guid.NewGuid();
        var claimed = await _db.Ownership.ClaimUnownedRecordsAsync(claimer);

        // Only ownerless rows move. A claim that swept up owned rows would hand
        // one user's cooks to another.
        Assert.Equal(0, claimed);
        Assert.Equal(owner, (await _db.Cooks.GetAsync(owned.Id))!.AccountId);
    }

    [Fact]
    public async Task Claiming_for_an_empty_account_is_refused()
    {
        // It would be a no-op that reads as success, leaving the rows invisible.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _db.Ownership.ClaimUnownedRecordsAsync(Guid.Empty));
    }

    [Fact]
    public void Every_table_carrying_an_account_id_is_in_the_claim_list()
    {
        // A table added later and missed here would silently lose its
        // pre-scoping rows, which is exactly the bug this whole class exists for.
        var tablesWithAccounts = typeof(RecordOwnership).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Humo.Core.Data.Records")
            .Where(t => t.GetProperty("AccountId") is not null)
            .Select(t => t.GetCustomAttributes(typeof(SQLite.TableAttribute), false)
                .Cast<SQLite.TableAttribute>()
                .Single()
                .Name)
            .Order()
            .ToList();

        Assert.Equal(tablesWithAccounts, RecordOwnership.OwnedTables.Order().ToList());
    }
}
