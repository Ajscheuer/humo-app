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

public class CookHistoryViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IUserSettings _settings = Substitute.For<IUserSettings>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CookHistoryViewModel CreateViewModel()
        => new(_db.SummaryServiceWith(_settings), _localizer, _navigation, _db.Clock);

    private Task<Cook> ACookAsync(MeatType meatType = MeatType.Brisket)
        => _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = meatType,
            WeightKg = 6,
        });

    [Fact]
    public async Task An_empty_history_says_so_rather_than_showing_nothing()
    {
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Items);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public async Task A_running_cook_does_not_appear_in_history()
    {
        await ACookAsync();
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasItems);
    }

    [Fact]
    public async Task Each_cook_is_listed_with_its_duration_and_a_key_that_resolves()
    {
        var cook = await ACookAsync(MeatType.PorkRibs);
        _db.Clock.Advance(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(20));
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Items);
        Assert.Equal("05:20", item.DurationDisplay);
        Assert.False(item.IsEstimated);
        Assert.NotEqual(item.MeatTypeKey, _localizer[item.MeatTypeKey]);
    }

    [Fact]
    public async Task A_long_cook_reads_in_total_hours()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(26) + TimeSpan.FromMinutes(10));
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        // Not "02:10" with the day silently dropped.
        Assert.Equal("26:10", Assert.Single(vm.Items).DurationDisplay);
    }

    [Fact]
    public async Task A_cook_is_listed_at_the_local_time_it_started()
    {
        // 06:00 UTC, which is midnight in the test clock's UTC-06:00 zone.
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        // Listing the stored UTC value would date an overnight cook to the
        // following morning.
        Assert.Equal(
            new DateTime(2026, 3, 14, 0, 0, 0),
            Assert.Single(vm.Items).StartedAtLocal.DateTime);
    }

    [Fact]
    public async Task Cooks_are_listed_newest_first()
    {
        var first = await ACookAsync(MeatType.PorkRibs);
        await _db.Service.FinishCookAsync(first.Id);

        _db.Clock.Advance(TimeSpan.FromDays(7));
        var second = await ACookAsync(MeatType.Brisket);
        await _db.Service.FinishCookAsync(second.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal([second.Id, first.Id], vm.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Opening_a_cook_navigates_with_its_id()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Items.Single());

        await _navigation.Received(1).GoToAsync(
            AppRoutes.CookSummaryFor(cook.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reloading_does_not_double_the_list()
    {
        var cook = await ACookAsync();
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.LoadCommand.ExecuteAsync(null);

        // The page reloads on every appearance, so this happens constantly.
        Assert.Single(vm.Items);
    }

    [Fact]
    public void A_cook_with_no_duration_shows_a_placeholder_not_a_zero()
    {
        var vm = CreateViewModel();

        // Reporting 00:00 would read as a cook that took no time.
        Assert.Equal(_localizer[AppStrings.Summary_Unknown], vm.FormatDuration(null));
    }

    [Fact]
    public async Task The_history_reads_in_Spanish_without_touching_the_data()
    {
        var cook = await ACookAsync(MeatType.PorkButt);
        _db.Clock.Advance(TimeSpan.FromHours(9));
        await _db.Service.FinishCookAsync(cook.Id);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        Assert.Equal("Historial", vm.Title);
        Assert.Equal("Paleta de cerdo", _localizer[Assert.Single(vm.Items).MeatTypeKey]);

        // The clock face does not translate.
        Assert.Equal("09:00", vm.Items.Single().DurationDisplay);
    }
}
