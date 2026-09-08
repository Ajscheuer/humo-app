using Humo.Core.Analytics;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.ViewModels;
using Humo.Shared.Analytics;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class CookInsightsViewModelTests
{
    private readonly StubAnalyticsClient _analytics = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly Localizer _localizer = new();
    private readonly Guid _cookId = Guid.NewGuid();

    [Fact]
    public async Task An_unusual_cook_is_listed()
    {
        _analytics.Response = AnalyticsFetch.Ok(Established() with
        {
            Anomalies =
            [
                new AnomalyFlag
                {
                    Metric = AnalyticsMetric.PitStability,
                    Direction = AnomalyDirection.Low,
                    Value = 40,
                    BaselineMean = 85,
                    BaselineStdDev = 5,
                    SampleSize = 12,
                },
            ],
        });

        var vm = await LoadedAsync();

        var insight = Assert.Single(vm.Items);
        Assert.Equal(_localizer[AppStrings.Analytics_PitStability], insight.MetricLabel);
        Assert.Equal(_localizer[AppStrings.Analytics_UnusuallyLow], insight.DirectionLabel);
        Assert.True(vm.HasInsights);
    }

    [Fact]
    public async Task An_ordinary_cook_says_so_rather_than_showing_nothing()
    {
        _analytics.Response = AnalyticsFetch.Ok(Established());

        var vm = await LoadedAsync();

        // An established baseline and nothing outside it is a result. A quiet
        // empty panel reads as broken.
        Assert.Empty(vm.Items);
        Assert.Equal(_localizer[AppStrings.Analytics_NothingUnusual], vm.Message);
    }

    [Fact]
    public async Task A_young_baseline_says_how_many_more_cooks_are_needed()
    {
        _analytics.Response = AnalyticsFetch.Ok(WithBaseline(sampleSize: 3));

        var vm = await LoadedAsync();

        Assert.Contains("5", vm.Message);
        Assert.False(vm.CanUpgrade);
    }

    [Fact]
    public async Task One_cook_short_reads_as_a_sentence_rather_than_a_one()
    {
        _analytics.Response = AnalyticsFetch.Ok(WithBaseline(sampleSize: 7));

        var vm = await LoadedAsync();

        // "1 more cooks" is the kind of thing that makes an app feel unfinished,
        // and Spanish needs a different sentence rather than a different number.
        Assert.Equal(_localizer[AppStrings.Analytics_BaselineBuildingOne], vm.Message);
    }

    [Fact]
    public async Task A_young_baseline_shows_no_flags_even_if_the_server_sent_some()
    {
        _analytics.Response = AnalyticsFetch.Ok(WithBaseline(sampleSize: 2) with
        {
            Anomalies =
            [
                new AnomalyFlag
                {
                    Metric = AnalyticsMetric.TimePerKg,
                    Direction = AnomalyDirection.High,
                    Value = 99,
                    BaselineMean = 2,
                    BaselineStdDev = 0.1,
                    SampleSize = 2,
                },
            ],
        });

        var vm = await LoadedAsync();

        // The server already withholds these. The screen not trusting a flag
        // that contradicts its own sample size is the second lock on the thing
        // that would teach a user to ignore the feature.
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task A_free_account_is_offered_the_upgrade()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);

        var vm = await LoadedAsync();

        Assert.True(vm.CanUpgrade);
        Assert.Equal(_localizer[AppStrings.Analytics_NeedsPro], vm.Message);
    }

    [Fact]
    public async Task Being_offline_is_not_mistaken_for_a_paywall()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);

        var vm = await LoadedAsync();

        // Collapsing these into one message is how an outage gets mistaken for
        // a paywall, and a paywall for a bug.
        Assert.False(vm.CanUpgrade);
        Assert.Equal(_localizer[AppStrings.Analytics_Offline], vm.Message);
    }

    [Fact]
    public async Task A_cook_that_has_not_synced_says_so()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.NotComputed);

        var vm = await LoadedAsync();

        Assert.False(vm.CanUpgrade);
        Assert.Equal(_localizer[AppStrings.Analytics_NotComputed], vm.Message);
    }

    [Fact]
    public async Task The_upgrade_offer_opens_the_paywall()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);

        var vm = await LoadedAsync();
        await vm.UpgradeCommand.ExecuteAsync(null);

        await _navigation.Received(1).GoToAsync(AppRoutes.Paywall, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Loading_again_replaces_rather_than_appends()
    {
        _analytics.Response = AnalyticsFetch.Ok(Established() with
        {
            Anomalies = [AFlag()],
        });

        var vm = await LoadedAsync();
        await vm.LoadCommand.ExecuteAsync(_cookId);

        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task An_upgrade_message_is_cleared_once_the_numbers_arrive()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);
        var vm = await LoadedAsync();

        Assert.True(vm.CanUpgrade);

        _analytics.Response = AnalyticsFetch.Ok(Established() with { Anomalies = [AFlag()] });
        await vm.LoadCommand.ExecuteAsync(_cookId);

        // Otherwise a subscriber who just bought Pro keeps the offer on screen
        // underneath their own insights.
        Assert.False(vm.CanUpgrade);
        Assert.Null(vm.Message);
    }

    [Fact]
    public async Task The_panel_reads_in_the_current_language()
    {
        _analytics.Response = AnalyticsFetch.Unavailable(AnalyticsUnavailable.NeedsPro);
        var vm = await LoadedAsync();

        var english = vm.Message;

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));
        await vm.LoadCommand.ExecuteAsync(_cookId);

        Assert.NotEqual(english, vm.Message);
        Assert.Equal(_localizer[AppStrings.Analytics_NeedsPro], vm.Message);
    }

    [Fact]
    public async Task The_cooks_number_is_formatted_for_the_readers_culture()
    {
        _analytics.Response = AnalyticsFetch.Ok(Established() with
        {
            Anomalies = [AFlag() with { Value = 3.5, BaselineMean = 2.1 }],
        });

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));
        var vm = await LoadedAsync();

        // Spanish writes a decimal comma. A hardcoded invariant format here
        // would quietly show every Spanish reader the wrong punctuation.
        Assert.Contains("3,5", Assert.Single(vm.Items).Detail);
    }

    private async Task<CookInsightsViewModel> LoadedAsync()
    {
        var vm = new CookInsightsViewModel(_analytics, _localizer, _navigation);
        await vm.LoadCommand.ExecuteAsync(_cookId);
        return vm;
    }

    private static AnomalyFlag AFlag() => new()
    {
        Metric = AnalyticsMetric.TimePerKg,
        Direction = AnomalyDirection.High,
        Value = 3.5,
        BaselineMean = 2.1,
        BaselineStdDev = 0.3,
        SampleSize = 10,
    };

    private CookAnalytics Established() => WithBaseline(sampleSize: 10);

    private CookAnalytics WithBaseline(int sampleSize) => new()
    {
        CookId = _cookId,
        Baseline = new BaselineStatus { SampleSize = sampleSize, RequiredSampleSize = 8 },
        ComputedAt = DateTimeOffset.UtcNow,
    };

    private sealed class StubAnalyticsClient : IAnalyticsClient
    {
        public AnalyticsFetch Response { get; set; } =
            AnalyticsFetch.Unavailable(AnalyticsUnavailable.Offline);

        public Task<AnalyticsFetch> GetAsync(Guid cookId, CancellationToken cancellationToken = default)
            => Task.FromResult(Response);
    }
}
