using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Analytics;

/// <summary>
/// What one cook adds up to. Computed on device from that cook alone.
/// <para>
/// Cross-cook analytics — baselines, anomaly flags, trends — are server-computed
/// and Pro-gated, and are deliberately not here. Everything in this record is
/// derivable from a single cook's own records, which is what keeps the summary
/// screen working offline and free for everyone.
/// </para>
/// </summary>
public sealed record CookStatistics
{
    /// <summary>How long the cook ran, or null while it is still running.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Duration divided by weight — the figure a cook actually plans with
    /// ("about 90 minutes a kilo on my offset"). Null until the cook finishes.
    /// </summary>
    public TimeSpan? TimePerKg { get; init; }

    /// <summary>
    /// True when the end time was inferred rather than observed — an
    /// auto-finished cook.
    /// <para>
    /// Such a cook plainly did not run for three days, so its duration is a
    /// guess. It is shown, flagged, and excluded from trends; silently mixing it
    /// into a time-per-kg baseline would poison the number the user plans with.
    /// </para>
    /// </summary>
    public bool IsEstimated { get; init; }

    /// <summary>Highest meat reading in Celsius, or null with no readings.</summary>
    public double? PeakMeatTempC { get; init; }

    /// <summary>Highest pit reading in Celsius, or null with no readings.</summary>
    public double? PeakPitTempC { get; init; }

    public int ReadingCount { get; init; }

    public int FuelEventCount { get; init; }

    /// <summary>
    /// Whether this cook may feed a time-per-kg trend. False for a cook still
    /// running and for one the app closed on its behalf.
    /// </summary>
    public bool CountsTowardTrends => Duration is not null && !IsEstimated;

    /// <summary>
    /// The duration figures for a cook, from the cook alone.
    /// <para>
    /// Separate from the readings so it can be reasoned about on its own: this is
    /// pure arithmetic over two timestamps and a weight, and every awkward case
    /// in it — a running cook, an auto-finished one, a clock that went backwards
    /// — is a decision rather than a calculation.
    /// </para>
    /// </summary>
    public static CookStatistics For(Cook cook)
    {
        ArgumentNullException.ThrowIfNull(cook);

        var duration = DurationOf(cook);

        return new CookStatistics
        {
            Duration = duration,
            TimePerKg = TimePerKilogram(duration, cook.WeightKg),
            IsEstimated = cook.FinishReason == CookFinishReason.AutoFinished,
        };
    }

    private static TimeSpan? DurationOf(Cook cook)
    {
        if (cook.FinishedAt is not { } finishedAt)
        {
            return null;
        }

        var elapsed = finishedAt - cook.StartedAt;

        // A negative duration means the two timestamps disagree about which came
        // first -- a device clock corrected mid-cook, or a record that arrived
        // from another device with skew. Reporting "-2 hours" is worse than
        // reporting nothing, and clamping to zero would invent a cook that took
        // no time. Absent is the honest answer.
        return elapsed < TimeSpan.Zero ? null : elapsed;
    }

    private static TimeSpan? TimePerKilogram(TimeSpan? duration, double weightKg)
    {
        if (duration is not { } elapsed || !double.IsFinite(weightKg) || weightKg <= 0)
        {
            return null;
        }

        return elapsed / weightKg;
    }
}
