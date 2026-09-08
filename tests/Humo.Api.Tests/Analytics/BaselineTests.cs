using Humo.Api.Analytics;
using Humo.Shared.Analytics;

namespace Humo.Api.Tests.Analytics;

public class BaselineTests
{
    [Fact]
    public void A_baseline_is_the_mean_and_spread_of_the_users_own_cooks()
    {
        var stats = Baseline.Compute([2, 4, 4, 4, 5, 5, 7, 9]);

        // Hand-checked: mean 5, population standard deviation 2.
        Assert.Equal(8, stats.SampleSize);
        Assert.Equal(5, stats.Mean);
        Assert.Equal(2, stats.StandardDeviation);
    }

    [Fact]
    public void No_cooks_produce_an_empty_baseline_rather_than_a_crash()
    {
        var stats = Baseline.Compute([]);

        Assert.Equal(0, stats.SampleSize);
        Assert.False(stats.IsEstablished);
    }

    [Fact]
    public void A_single_cook_has_no_spread()
    {
        var stats = Baseline.Compute([7]);

        Assert.Equal(1, stats.SampleSize);
        Assert.Equal(7, stats.Mean);
        Assert.Equal(0, stats.StandardDeviation);
    }

    [Fact]
    public void Values_that_are_not_numbers_are_left_out()
    {
        // A metric that divided by zero somewhere upstream must not turn the
        // whole baseline into NaN and silently disable anomaly detection.
        var stats = Baseline.Compute([4, double.NaN, 6, double.PositiveInfinity]);

        Assert.Equal(2, stats.SampleSize);
        Assert.Equal(5, stats.Mean);
    }

    [Fact]
    public void Seven_cooks_is_not_yet_a_baseline()
    {
        Assert.False(Baseline.Compute(Enumerable.Repeat(5.0, 7)).IsEstablished);
    }

    [Fact]
    public void Eight_cooks_is()
    {
        // The boundary from product-spec.md 6. An off-by-one here either flags
        // on noise or withholds a baseline the user has earned.
        Assert.True(Baseline.Compute(Enumerable.Repeat(5.0, 8)).IsEstablished);
    }
}

public class AnomalyComparisonTests
{
    private static readonly BaselineStatistics Established = new(SampleSize: 10, Mean: 100, StandardDeviation: 10);

    [Fact]
    public void A_cook_well_outside_the_usual_range_is_flagged()
    {
        var flag = Baseline.Compare(AnalyticsMetric.TimePerKg, 130, Established);

        Assert.NotNull(flag);
        Assert.Equal(AnomalyDirection.High, flag.Direction);
        Assert.Equal(130, flag.Value);
        Assert.Equal(100, flag.BaselineMean);
        Assert.Equal(10, flag.SampleSize);
    }

    [Fact]
    public void A_cook_well_below_the_usual_range_is_flagged_low()
    {
        var flag = Baseline.Compare(AnalyticsMetric.PitStability, 70, Established);

        Assert.NotNull(flag);
        Assert.Equal(AnomalyDirection.Low, flag.Direction);
    }

    [Fact]
    public void A_typical_cook_is_not_flagged()
    {
        Assert.Null(Baseline.Compare(AnalyticsMetric.TimePerKg, 105, Established));
    }

    [Fact]
    public void Exactly_two_sigma_is_not_yet_unusual()
    {
        // The boundary. "Beyond ±2σ" is past it, not at it, and flagging the
        // edge makes roughly one cook in twenty an anomaly by construction.
        Assert.Null(Baseline.Compare(AnalyticsMetric.TimePerKg, 120, Established));
        Assert.Null(Baseline.Compare(AnalyticsMetric.TimePerKg, 80, Established));
    }

    [Fact]
    public void Just_past_two_sigma_is()
    {
        Assert.NotNull(Baseline.Compare(AnalyticsMetric.TimePerKg, 120.1, Established));
        Assert.NotNull(Baseline.Compare(AnalyticsMetric.TimePerKg, 79.9, Established));
    }

    [Fact]
    public void Nothing_is_flagged_before_the_baseline_is_established()
    {
        var young = new BaselineStatistics(SampleSize: 3, Mean: 100, StandardDeviation: 10);

        // Wildly outside the mean, and still silent. A false alarm on three
        // cooks teaches the user to ignore the feature permanently.
        Assert.Null(Baseline.Compare(AnalyticsMetric.TimePerKg, 500, young));
    }

    [Fact]
    public void A_baseline_with_no_spread_flags_nothing()
    {
        // Eight identical cooks. Any difference at all is infinitely many sigmas
        // out, which would flag a rounding error.
        var identical = new BaselineStatistics(SampleSize: 8, Mean: 100, StandardDeviation: 0);

        Assert.Null(Baseline.Compare(AnalyticsMetric.TimePerKg, 100.0001, identical));
    }

    [Fact]
    public void A_metric_this_cook_does_not_have_is_not_flagged()
    {
        // A cook with no pit data has no stability score. Absent is not zero,
        // and treating it as zero would flag every one of them.
        Assert.Null(Baseline.Compare(AnalyticsMetric.PitStability, null, Established));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_value_that_is_not_a_number_is_not_flagged(double value)
    {
        Assert.Null(Baseline.Compare(AnalyticsMetric.FuelEfficiency, value, Established));
    }
}
