using Humo.Shared.Analytics;

namespace Humo.Api.Analytics;

/// <summary>What a user usually does, for one metric on one (meat, rig) pair.</summary>
public readonly record struct BaselineStatistics(int SampleSize, double Mean, double StandardDeviation)
{
    public bool IsEstablished => SampleSize >= AnalyticsPolicy.MinimumBaselineSample;
}

/// <summary>
/// Baselines and the anomaly comparison, as pure functions.
/// <para>
/// Kept out of the service that reads the database so the statistics can be
/// tested against hand-checked numbers rather than through EF.
/// </para>
/// </summary>
public static class Baseline
{
    /// <summary>
    /// Mean and standard deviation of a metric across a user's own cooks.
    /// <para>
    /// The population standard deviation, not the sample one: these are all the
    /// cooks the user has done for that pair, not a sample drawn from a larger
    /// set. With eight cooks the difference is small, but dividing by n−1 here
    /// would be answering a question nobody asked.
    /// </para>
    /// </summary>
    public static BaselineStatistics Compute(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sample = values.Where(double.IsFinite).ToList();

        if (sample.Count == 0)
        {
            return new BaselineStatistics(0, 0, 0);
        }

        var mean = sample.Average();
        var variance = sample.Average(v => (v - mean) * (v - mean));

        return new BaselineStatistics(sample.Count, mean, Math.Sqrt(variance));
    }

    /// <summary>
    /// Whether this cook's value is unusual for this user, or null when it is
    /// not — or when the baseline is not entitled to an opinion yet.
    /// </summary>
    public static AnomalyFlag? Compare(AnalyticsMetric metric, double? value, BaselineStatistics baseline)
    {
        if (value is not { } observed || !double.IsFinite(observed))
        {
            return null;
        }

        if (!baseline.IsEstablished)
        {
            // Below the minimum, no flags at all. A false "this cook is unusual"
            // on a baseline of three teaches the user to ignore the feature
            // permanently, which is worse than silence.
            return null;
        }

        if (baseline.StandardDeviation <= 0)
        {
            // Every cook so far produced the same number. Any difference is
            // infinitely many sigmas away, which would flag a rounding error as
            // an anomaly.
            return null;
        }

        var deviations = (observed - baseline.Mean) / baseline.StandardDeviation;

        if (Math.Abs(deviations) <= AnalyticsPolicy.AnomalySigma)
        {
            return null;
        }

        return new AnomalyFlag
        {
            Metric = metric,
            Direction = deviations > 0 ? AnomalyDirection.High : AnomalyDirection.Low,
            Value = observed,
            BaselineMean = baseline.Mean,
            BaselineStdDev = baseline.StandardDeviation,
            SampleSize = baseline.SampleSize,
        };
    }
}
