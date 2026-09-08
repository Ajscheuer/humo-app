namespace Humo.Shared.Analytics;

/// <summary>The metrics a cook is compared against its own baseline on.</summary>
public enum AnalyticsMetric
{
    TimePerKg = 0,
    StallDuration = 1,
    PitStability = 2,
    FuelEfficiency = 3,
}

/// <summary>Which side of normal a metric fell on.</summary>
public enum AnomalyDirection
{
    /// <summary>Below the user's own mean by more than the threshold.</summary>
    Low = 0,

    /// <summary>Above it.</summary>
    High = 1,
}

/// <summary>
/// One metric on one cook sitting outside the user's own normal range.
/// <para>
/// Always against the user's own baseline for that (meat type, equipment) pair,
/// never a global population — <c>product-spec.md</c> §6 is explicit that the
/// whole point is "unusual <em>for you</em>".
/// </para>
/// </summary>
public sealed record AnomalyFlag
{
    public required AnalyticsMetric Metric { get; init; }

    public required AnomalyDirection Direction { get; init; }

    /// <summary>What this cook did.</summary>
    public required double Value { get; init; }

    /// <summary>What this user usually does.</summary>
    public required double BaselineMean { get; init; }

    public required double BaselineStdDev { get; init; }

    /// <summary>How many cooks the comparison stands on.</summary>
    public required int SampleSize { get; init; }
}

/// <summary>
/// How close a (meat type, equipment) pair is to having a usable baseline.
/// <para>
/// Carried even when it is not ready, because that is the honest UI:
/// <c>product-spec.md</c> §6 wants the screen to say how many more cooks are
/// needed rather than showing an empty analytics panel.
/// </para>
/// </summary>
public sealed record BaselineStatus
{
    public required int SampleSize { get; init; }

    public required int RequiredSampleSize { get; init; }

    public bool IsEstablished => SampleSize >= RequiredSampleSize;

    /// <summary>How many more cooks of this kind are needed. Zero once established.</summary>
    public int CooksNeeded => Math.Max(0, RequiredSampleSize - SampleSize);
}

/// <summary>
/// The server's cached numbers for one cook.
/// <para>
/// Read-only on the client: every field here is computed from records the user
/// entered and stored separately from them, so recomputation never rewrites
/// anything a person typed.
/// </para>
/// </summary>
public sealed record CookAnalytics
{
    public required Guid CookId { get; init; }

    /// <summary>Null while the cook is still running.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Hours per kilogram. The metric is metric; the display converts.</summary>
    public double? TimePerKg { get; init; }

    public DateTimeOffset? StallStartedAt { get; init; }

    public DateTimeOffset? StallEndedAt { get; init; }

    public TimeSpan? StallDuration { get; init; }

    /// <summary>0–100, higher is a steadier fire. Null without enough pit readings.</summary>
    public double? PitStabilityScore { get; init; }

    /// <summary>Fuel events per hour per 100 °C of lift. Lower is better.</summary>
    public double? FuelEfficiency { get; init; }

    /// <summary>Empty when nothing was unusual, or when the baseline is not established yet.</summary>
    public IReadOnlyList<AnomalyFlag> Anomalies { get; init; } = [];

    public required BaselineStatus Baseline { get; init; }

    /// <summary>When the server last computed this.</summary>
    public required DateTimeOffset ComputedAt { get; init; }
}
