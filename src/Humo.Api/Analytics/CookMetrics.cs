namespace Humo.Api.Analytics;

/// <summary>One temperature reading, as the metrics need it.</summary>
public readonly record struct Reading(DateTimeOffset At, double TempC);

/// <summary>The plateau found in a cook, if there was one.</summary>
public readonly record struct Stall(DateTimeOffset StartedAt, DateTimeOffset EndedAt)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

/// <summary>
/// The per-cook numbers, as pure functions over readings.
/// <para>
/// No database, no clock, no entity types. Every one of these is arithmetic
/// whose awkward cases are decisions rather than calculations — an empty series,
/// a single reading, readings out of order, a cook with no pit data at all — and
/// each of those is a test rather than a comment.
/// </para>
/// </summary>
public static class CookMetrics
{
    /// <summary>
    /// The longest run inside the plateau band where the meat rose no faster
    /// than the stall rate, or null when there wasn't one.
    /// <para>
    /// Readings are sorted first: <c>recordedAt</c> is editable, so a cook
    /// correcting a reading they logged late genuinely produces out-of-order
    /// input, and comparing consecutive entries in arrival order would measure
    /// nonsense.
    /// </para>
    /// </summary>
    public static Stall? FindStall(IEnumerable<Reading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var ordered = readings.OrderBy(r => r.At).ToList();

        Stall? longest = null;
        var runStart = -1;

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            if (IsStalledStep(previous, current))
            {
                // Extend the current run, or open one at the earlier reading.
                if (runStart < 0)
                {
                    runStart = i - 1;
                }

                var candidate = new Stall(ordered[runStart].At, current.At);

                if (candidate.Duration >= AnalyticsPolicy.MinimumStallDuration
                    && candidate.Duration > (longest?.Duration ?? TimeSpan.Zero))
                {
                    longest = candidate;
                }
            }
            else
            {
                runStart = -1;
            }
        }

        return longest;
    }

    private static bool IsStalledStep(Reading previous, Reading current)
    {
        var hours = (current.At - previous.At).TotalHours;

        // Two readings at the same instant say nothing about a rate. Dividing by
        // zero here would produce an infinite or NaN rise and, worse, one that
        // compares as "not greater than" the threshold and reads as a stall.
        if (hours <= 0)
        {
            return false;
        }

        var inBand = InStallBand(previous.TempC) && InStallBand(current.TempC);

        return inBand
            && (current.TempC - previous.TempC) / hours <= AnalyticsPolicy.StallMaxRiseCPerHour;
    }

    private static bool InStallBand(double tempC)
        => tempC >= AnalyticsPolicy.StallBandLowC && tempC <= AnalyticsPolicy.StallBandHighC;

    /// <summary>
    /// How steady the fire was, from 0 to 100, or null when there were too few
    /// readings to say.
    /// <para>
    /// Dispersion around the cook's own median, normalized so higher is steadier
    /// — the spec's definition, and the median rather than the mean because one
    /// reading taken with the lid open should not redefine what "normal" was for
    /// the whole cook.
    /// </para>
    /// </summary>
    public static double? PitStabilityScore(IEnumerable<Reading> pitReadings)
    {
        ArgumentNullException.ThrowIfNull(pitReadings);

        var temps = pitReadings.Select(r => r.TempC).ToList();

        if (temps.Count < AnalyticsPolicy.MinimumPitReadingsForStability)
        {
            // A perfect score from two readings is a lie with a number attached.
            return null;
        }

        var median = Median(temps);

        // Mean absolute deviation, not standard deviation: one door-open spike
        // should cost a steady fire a little, not square its way into dominating
        // the score.
        var deviation = temps.Average(t => Math.Abs(t - median));

        if (median <= 0)
        {
            // A median at or below 0 °C is not a fire. Dividing by it would
            // invert or explode the score.
            return null;
        }

        // Relative deviation, so a 10 °C wobble on a 110 °C smoke and on a
        // 250 °C parrilla are not scored as the same failure.
        var score = 100.0 * (1.0 - (deviation / median));

        return Math.Clamp(score, 0.0, 100.0);
    }

    /// <summary>
    /// Fuel events per hour, normalized by how hard the fire was working, or
    /// null when the cook was too short or had no pit data.
    /// <para>
    /// The normalization is the point: <c>product-spec.md</c> §6 wants a cold
    /// windy day not to look like poor technique. Feeding a fire twice an hour
    /// to hold 120 °C at −5 °C ambient is good work; the same cadence to hold
    /// 120 °C at 30 °C ambient is not.
    /// </para>
    /// </summary>
    /// <returns>
    /// Fuel events per hour per 100 °C of pit-over-ambient lift. Lower is more
    /// efficient.
    /// </returns>
    public static double? FuelEfficiency(
        int fuelEventCount,
        TimeSpan duration,
        double? averagePitTempC,
        double? averageAmbientTempC)
    {
        if (duration <= TimeSpan.Zero || averagePitTempC is not { } pit)
        {
            return null;
        }

        // Without a measured ambient, assume a mild day rather than refusing to
        // score: a missing ambient is the common case, and 20 °C is a far
        // smaller error than treating the lift as the whole pit temperature.
        var lift = pit - (averageAmbientTempC ?? 20.0);

        if (lift <= 0)
        {
            // The pit is no warmer than the air around it. Nothing was being
            // held, so there is no efficiency to report.
            return null;
        }

        var perHour = fuelEventCount / duration.TotalHours;

        return perHour / (lift / 100.0);
    }

    /// <summary>Hours of cook per kilogram, or null without a duration or a weight.</summary>
    public static double? TimePerKg(TimeSpan? duration, double weightKg)
        => duration is { } elapsed && elapsed > TimeSpan.Zero && weightKg > 0
            ? elapsed.TotalHours / weightKg
            : null;

    /// <summary>The middle value, averaging the two middles for an even count.</summary>
    internal static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}
