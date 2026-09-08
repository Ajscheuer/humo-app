namespace Humo.Api.Analytics;

/// <summary>The numbers the analytics turn on, in one place.</summary>
public static class AnalyticsPolicy
{
    /// <summary>
    /// How many cooks a (meat type, equipment) pair needs before anomaly flags
    /// appear at all.
    /// <para>
    /// Eight, from <c>product-spec.md</c> §6, and it is a judgement call rather
    /// than a derived number: roughly where ±2σ stops being dominated by
    /// sampling noise, while still being reachable within a season for a monthly
    /// cook. Below it the UI says how many more are needed and shows no flags —
    /// a false "this cook is unusual" on a baseline of three teaches the user to
    /// ignore the feature permanently.
    /// </para>
    /// </summary>
    public const int MinimumBaselineSample = 8;

    /// <summary>How far from the user's own mean counts as unusual.</summary>
    public const double AnomalySigma = 2.0;

    /// <summary>
    /// The meat temperature range the stall is looked for in.
    /// <para>
    /// Not specified numerically in the docs; these are defensible defaults and
    /// recorded as an open question. Roughly 60–80 °C (140–176 °F) covers where
    /// brisket and pork shoulder actually plateau. Looking outside it would let
    /// the slow climb at the very start of a cook read as a stall.
    /// </para>
    /// </summary>
    public const double StallBandLowC = 60.0;

    /// <inheritdoc cref="StallBandLowC"/>
    public const double StallBandHighC = 80.0;

    /// <summary>
    /// The rise rate at or below which the meat counts as stalled, in °C per
    /// hour. Evaporative cooling holds the meat close to flat; a degree an hour
    /// is comfortably below a cook that is still climbing.
    /// </summary>
    public const double StallMaxRiseCPerHour = 1.0;

    /// <summary>
    /// The shortest interval reported as a stall. Two readings twenty minutes
    /// apart that happen to match are noise, not a plateau.
    /// </summary>
    public static readonly TimeSpan MinimumStallDuration = TimeSpan.FromMinutes(45);

    /// <summary>
    /// The fewest pit readings a stability score can be computed from. A
    /// "perfectly steady fire" derived from two readings is a lie with a number
    /// attached.
    /// </summary>
    public const int MinimumPitReadingsForStability = 5;
}
