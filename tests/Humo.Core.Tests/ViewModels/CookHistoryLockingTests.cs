using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Settings;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

/// <summary>
/// The free history limit as the list applies it. product-spec.md §5.1: the
/// most recent cooks are open, older ones are listed but locked, and none of it
/// is ever hidden.
/// </summary>
public class CookHistoryLockingTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_free_account_keeps_its_most_recent_cooks()
    {
        await ACookHistoryOf(5);

        var vm = await LoadedWith(FakeEntitlements.Free(3));

        Assert.Equal([false, false, false, true, true], vm.Items.Select(i => i.IsLocked));
    }

    [Fact]
    public async Task A_locked_cook_is_still_listed_dated_and_named()
    {
        await ACookHistoryOf(3);

        var vm = await LoadedWith(FakeEntitlements.Free(1));

        // Never hidden: an empty list reads as data loss, a locked list reads as
        // an offer.
        Assert.Equal(3, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.NotEqual(default, i.StartedAtLocal));
        Assert.All(vm.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.MeatTypeKey)));
    }

    [Fact]
    public async Task A_history_within_the_limit_locks_nothing()
    {
        await ACookHistoryOf(3);

        var vm = await LoadedWith(FakeEntitlements.Free(5));

        Assert.All(vm.Items, i => Assert.False(i.IsLocked));
        Assert.False(vm.HasLockedItems);
    }

    [Fact]
    public async Task A_history_exactly_at_the_limit_locks_nothing()
    {
        await ACookHistoryOf(5);

        var vm = await LoadedWith(FakeEntitlements.Free(5));

        // The boundary is where an off-by-one would lock the fifth cook of a
        // user who is told they keep five.
        Assert.All(vm.Items, i => Assert.False(i.IsLocked));
    }

    [Fact]
    public async Task One_cook_past_the_limit_is_the_only_locked_one()
    {
        await ACookHistoryOf(6);

        var vm = await LoadedWith(FakeEntitlements.Free(5));

        Assert.Single(vm.Items, i => i.IsLocked);
        Assert.True(vm.Items[5].IsLocked);
    }

    [Fact]
    public async Task A_pro_account_has_nothing_locked()
    {
        await ACookHistoryOf(9);

        var vm = await LoadedWith(FakeEntitlements.Pro());

        Assert.All(vm.Items, i => Assert.False(i.IsLocked));
        Assert.False(vm.HasLockedItems);
    }

    [Fact]
    public async Task An_entitlement_nobody_has_told_us_locks_nothing()
    {
        await ACookHistoryOf(9);

        var vm = await LoadedWith(FakeEntitlements.Unknown());

        // A fresh install that has not been online, or a subscriber on a plane.
        // Locking on a guess would hide their own history from them.
        Assert.All(vm.Items, i => Assert.False(i.IsLocked));
    }

    [Fact]
    public async Task A_limit_of_zero_locks_everything_but_hides_nothing()
    {
        await ACookHistoryOf(3);

        var vm = await LoadedWith(FakeEntitlements.Free(0));

        Assert.All(vm.Items, i => Assert.True(i.IsLocked));
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task Opening_an_unlocked_cook_opens_the_cook()
    {
        await ACookHistoryOf(2);
        var vm = await LoadedWith(FakeEntitlements.Free(5));

        await vm.OpenCommand.ExecuteAsync(vm.Items[0]);

        await _navigation.Received(1).GoToAsync(
            Arg.Is<string>(r => r.StartsWith(AppRoutes.CookSummary, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_a_locked_cook_opens_the_offer_instead()
    {
        await ACookHistoryOf(2);
        var vm = await LoadedWith(FakeEntitlements.Free(1));

        await vm.OpenCommand.ExecuteAsync(vm.Items[1]);

        // Opening it and showing an empty summary is the "reads as a bug"
        // failure 5.1 exists to avoid.
        await _navigation.Received(1).GoToAsync(AppRoutes.Paywall, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_a_locked_cook_does_not_also_open_the_cook()
    {
        await ACookHistoryOf(2);
        var vm = await LoadedWith(FakeEntitlements.Free(1));

        await vm.OpenCommand.ExecuteAsync(vm.Items[1]);

        await _navigation.DidNotReceive().GoToAsync(
            Arg.Is<string>(r => r.StartsWith(AppRoutes.CookSummary, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upgrading_unlocks_the_list_on_the_next_load()
    {
        await ACookHistoryOf(4);
        var entitlements = FakeEntitlements.Free(1);
        var vm = await LoadedWith(entitlements);

        Assert.True(vm.HasLockedItems);

        entitlements.Current = FakeEntitlements.Pro().Current;
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasLockedItems);
    }

    private async Task<CookHistoryViewModel> LoadedWith(FakeEntitlements entitlements)
    {
        var vm = new CookHistoryViewModel(
            _db.SummaryServiceWith(_settings), _localizer, _navigation, _db.Clock, entitlements);

        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    /// <summary>Finished cooks, one per hour, so the ordering is unambiguous.</summary>
    private async Task ACookHistoryOf(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var cook = await _db.Service.StartCookAsync(new StartCookRequest
            {
                MeatType = MeatType.Brisket,
                WeightKg = 6,
            });

            _db.Clock.Advance(TimeSpan.FromHours(1));
            await _db.Service.FinishCookAsync(cook.Id);
            _db.Clock.Advance(TimeSpan.FromHours(1));
        }
    }
}
