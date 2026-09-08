using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Analytics;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Shared.Analytics;

namespace Humo.Core.ViewModels;

/// <summary>One thing that was unusual about this cook, ready to display.</summary>
/// <param name="MetricLabel">Which measurement, in the current language.</param>
/// <param name="DirectionLabel">Higher or lower than usual.</param>
/// <param name="Detail">The cook's value against the user's own average.</param>
public sealed record InsightItem(string MetricLabel, string DirectionLabel, string Detail);

/// <summary>
/// The Pro panel on the cook summary: what was unusual about this cook, or the
/// honest reason there is nothing to show.
/// <para>
/// Four different empty states, deliberately — offline, needs Pro, not synced
/// yet, and not enough cooks. Collapsing them into one "no data" message is how
/// a paywall gets mistaken for a bug and an outage gets mistaken for a paywall.
/// </para>
/// </summary>
public sealed partial class CookInsightsViewModel : ObservableObject
{
    private readonly IAnalyticsClient _analytics;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public CookInsightsViewModel(
        IAnalyticsClient analytics,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _analytics = analytics;
        _localizer = localizer;
        _navigation = navigation;
    }

    /// <summary>What was unusual about this cook. Empty is a normal state.</summary>
    public ObservableCollection<InsightItem> Items { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The reason there is nothing to show, or null once there is.</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>True when the message is an offer rather than an explanation.</summary>
    [ObservableProperty]
    private bool _canUpgrade;

    public string Title => _localizer[AppStrings.Analytics_Title];

    public bool HasInsights => Items.Count > 0;

    [RelayCommand]
    private async Task LoadAsync(Guid cookId, CancellationToken cancellationToken)
    {
        Items.Clear();
        Message = null;
        CanUpgrade = false;
        IsBusy = true;

        try
        {
            var fetched = await _analytics.GetAsync(cookId, cancellationToken);

            if (!fetched.Succeeded)
            {
                Message = _localizer[MessageFor(fetched.Reason)];
                CanUpgrade = fetched.Reason == AnalyticsUnavailable.NeedsPro;
                return;
            }

            Show(fetched.Value!);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasInsights));
        }
    }

    [RelayCommand]
    private Task UpgradeAsync(CancellationToken cancellationToken)
        => _navigation.GoToAsync(AppRoutes.Paywall, cancellationToken);

    private void Show(CookAnalytics analytics)
    {
        if (!analytics.Baseline.IsEstablished)
        {
            // The honest state product-spec.md §6 asks for: say how many more
            // cooks are needed rather than showing an empty panel, and show no
            // flags at all. A false "this cook is unusual" on a baseline of
            // three teaches the user to ignore the feature permanently.
            Message = analytics.Baseline.CooksNeeded == 1
                ? _localizer[AppStrings.Analytics_BaselineBuildingOne]
                : string.Format(
                    _localizer.CurrentCulture,
                    _localizer[AppStrings.Analytics_BaselineBuilding],
                    analytics.Baseline.CooksNeeded);

            return;
        }

        foreach (var anomaly in analytics.Anomalies)
        {
            Items.Add(new InsightItem(
                _localizer[LabelFor(anomaly.Metric)],
                _localizer[anomaly.Direction == AnomalyDirection.High
                    ? AppStrings.Analytics_UnusuallyHigh
                    : AppStrings.Analytics_UnusuallyLow],
                Detail(anomaly)));
        }

        if (Items.Count == 0)
        {
            // An established baseline and nothing outside it is a result, not an
            // absence. Saying so is what stops a quiet panel reading as broken.
            Message = _localizer[AppStrings.Analytics_NothingUnusual];
        }
    }

    /// <summary>
    /// The cook's value against the user's own average, both to one decimal.
    /// Culture-formatted: a Spanish reader expects a comma.
    /// </summary>
    private string Detail(AnomalyFlag anomaly)
        => string.Create(
            _localizer.CurrentCulture,
            $"{anomaly.Value:0.#} · {anomaly.BaselineMean:0.#}");

    private static string LabelFor(AnalyticsMetric metric) => metric switch
    {
        AnalyticsMetric.StallDuration => AppStrings.Analytics_Stall,
        AnalyticsMetric.PitStability => AppStrings.Analytics_PitStability,
        AnalyticsMetric.FuelEfficiency => AppStrings.Analytics_FuelEfficiency,

        // Time per weight already has a label on the summary screen above, and
        // it is the one metric whose unit follows the user's weight setting.
        _ => AppStrings.Summary_TimePerKg,
    };

    private static string MessageFor(AnalyticsUnavailable reason) => reason switch
    {
        AnalyticsUnavailable.NeedsPro => AppStrings.Analytics_NeedsPro,
        AnalyticsUnavailable.NotComputed => AppStrings.Analytics_NotComputed,
        _ => AppStrings.Analytics_Offline,
    };
}
