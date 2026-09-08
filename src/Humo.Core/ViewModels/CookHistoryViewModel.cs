using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Analytics;
using Humo.Core.Entitlements;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Services;
using Humo.Core.Time;

namespace Humo.Core.ViewModels;

/// <summary>
/// One finished cook as the history list shows it.
/// </summary>
/// <param name="MeatTypeKey">A resource key — the list reads in the current language.</param>
/// <param name="StartedAtLocal">The cook's start, in the user's zone.</param>
/// <param name="DurationDisplay">Hours and minutes, or an em dash when unknown.</param>
/// <param name="IsEstimated">
/// True for an auto-finished cook, whose end time was inferred rather than
/// observed. Shown so a user is never quietly told a guess is a measurement.
/// </param>
/// <param name="IsLocked">
/// True for a cook beyond the free limit. It is still listed, dated and named —
/// product-spec.md 5.1 is explicit that these are never hidden, because an empty
/// list reads as data loss and a locked list reads as an offer.
/// </param>
public sealed record CookHistoryItem(
    Guid Id,
    string MeatTypeKey,
    DateTimeOffset StartedAtLocal,
    string DurationDisplay,
    bool IsEstimated,
    bool IsLocked);

/// <summary>The list of finished cooks, newest first.</summary>
public sealed partial class CookHistoryViewModel : ObservableObject
{
    private readonly ICookSummaryService _summaries;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IClientEntitlementService _entitlements;

    public CookHistoryViewModel(
        ICookSummaryService summaries,
        ILocalizer localizer,
        INavigationService navigation,
        IClock clock,
        IClientEntitlementService entitlements)
    {
        _summaries = summaries;
        _localizer = localizer;
        _navigation = navigation;
        _clock = clock;
        _entitlements = entitlements;
    }

    public ObservableCollection<CookHistoryItem> Items { get; } = [];

    public string Title => _localizer[AppStrings.History_Title];

    public bool HasItems => Items.Count > 0;

    /// <summary>True when anything in the list is behind the paywall.</summary>
    public bool HasLockedItems => Items.Any(i => i.IsLocked);

    public string LockedExplanation => _localizer[AppStrings.History_LockedExplanation];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var cooks = await _summaries.GetHistoryAsync(cancellationToken);

        // Null when the server has never said. Locking on a guess would hide a
        // subscriber's own history from them the first time they open the app
        // on a plane, so nothing is locked until the number is known.
        var limit = _entitlements.FreeCookHistoryLimit;

        Items.Clear();
        foreach (var (cook, index) in cooks.Select((c, i) => (c, i)))
        {
            var statistics = CookStatistics.For(cook);

            // The list is newest first, so the free allowance is the first N.
            Items.Add(new CookHistoryItem(
                cook.Id,
                EnumDisplay.KeyFor(cook.MeatType),
                TimeZoneInfo.ConvertTime(cook.StartedAt, _clock.LocalTimeZone),
                FormatDuration(statistics.Duration),
                statistics.IsEstimated,
                IsLocked: limit is { } free && index >= free));
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasLockedItems));
    }

    [RelayCommand]
    private Task OpenAsync(CookHistoryItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        // A locked cook opens the offer rather than the summary. Opening it and
        // showing an empty screen would be the "reads as a bug" failure 5.1 is
        // written to avoid.
        return item.IsLocked
            ? _navigation.GoToAsync(AppRoutes.Paywall, cancellationToken)
            : _navigation.GoToAsync(AppRoutes.CookSummaryFor(item.Id), cancellationToken);
    }

    /// <summary>
    /// Hours and minutes, zero-padded, or an em dash when there is no duration.
    /// <para>
    /// Total hours rather than <see cref="TimeSpan.Hours"/>: a 26-hour brisket
    /// reads "26:10", not "02:10" with the day silently dropped. The digits are
    /// culture-invariant; the labels beside them are not, and come from resources.
    /// </para>
    /// </summary>
    internal string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } elapsed)
        {
            return _localizer[AppStrings.Summary_Unknown];
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}");
    }
}
