using Humo.Core.Analytics;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Analytics;

public class CookStatisticsTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 3, 14, 6, 0, 0, TimeSpan.Zero);

    private static Cook ACook(
        TimeSpan? ran = null,
        double weightKg = 6,
        CookFinishReason? finishReason = CookFinishReason.Manual) => new()
    {
        MeatType = MeatType.Brisket,
        WeightKg = weightKg,
        StartedAt = Started,
        FinishedAt = ran is { } elapsed ? Started + elapsed : null,
        FinishReason = ran is null ? null : finishReason,
    };

    [Fact]
    public void A_finished_cook_reports_how_long_it_ran()
    {
        var statistics = CookStatistics.For(ACook(ran: TimeSpan.FromHours(13.5)));

        Assert.Equal(TimeSpan.FromHours(13.5), statistics.Duration);
    }

    [Fact]
    public void A_running_cook_has_no_duration_yet()
    {
        var statistics = CookStatistics.For(ACook());

        // Not zero: a cook that is still going has no total, and reporting 00:00
        // would read as one that took no time.
        Assert.Null(statistics.Duration);
        Assert.Null(statistics.TimePerKg);
        Assert.False(statistics.CountsTowardTrends);
    }

    [Fact]
    public void Time_per_kilo_divides_the_run_by_the_weight()
    {
        var statistics = CookStatistics.For(ACook(ran: TimeSpan.FromHours(12), weightKg: 6));

        // The figure a cook actually plans with: "about two hours a kilo".
        Assert.Equal(TimeSpan.FromHours(2), statistics.TimePerKg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_nonsense_weight_yields_no_time_per_kilo(double weightKg)
    {
        var statistics = CookStatistics.For(
            ACook(ran: TimeSpan.FromHours(12), weightKg: weightKg));

        // Dividing by zero gives infinity and by NaN gives NaN, either of which
        // would render as a garbage figure beside real ones.
        Assert.Equal(TimeSpan.FromHours(12), statistics.Duration);
        Assert.Null(statistics.TimePerKg);
    }

    [Fact]
    public void A_finish_time_before_the_start_yields_no_duration()
    {
        var cook = ACook(ran: TimeSpan.FromHours(-2));

        var statistics = CookStatistics.For(cook);

        // A device clock corrected mid-cook, or a record from another device with
        // skew. "-2 hours" is worse than nothing, and clamping to zero would
        // invent a cook that took no time.
        Assert.Null(statistics.Duration);
        Assert.Null(statistics.TimePerKg);
    }

    [Fact]
    public void A_cook_that_finished_the_instant_it_started_reports_zero_not_null()
    {
        var statistics = CookStatistics.For(ACook(ran: TimeSpan.Zero));

        // The boundary next to the negative case: zero is a real, if odd,
        // duration and must not be swept up by the guard against negatives.
        Assert.Equal(TimeSpan.Zero, statistics.Duration);
        Assert.Equal(TimeSpan.Zero, statistics.TimePerKg);
        Assert.True(statistics.CountsTowardTrends);
    }

    [Fact]
    public void An_auto_finished_cook_is_marked_as_estimated()
    {
        var statistics = CookStatistics.For(ACook(
            ran: TimeSpan.FromHours(30),
            finishReason: CookFinishReason.AutoFinished));

        // Its end time was inferred, not observed. The duration is still shown --
        // flagged -- but it must not feed a time-per-kg baseline.
        Assert.Equal(TimeSpan.FromHours(30), statistics.Duration);
        Assert.True(statistics.IsEstimated);
        Assert.False(statistics.CountsTowardTrends);
    }

    [Fact]
    public void A_manually_finished_cook_counts_toward_trends()
    {
        var statistics = CookStatistics.For(ACook(ran: TimeSpan.FromHours(13)));

        Assert.False(statistics.IsEstimated);
        Assert.True(statistics.CountsTowardTrends);
    }

    [Fact]
    public void A_cook_spanning_a_daylight_saving_change_measures_real_elapsed_time()
    {
        // Both instants are UTC, so a spring-forward in the user's zone changes
        // what the clock on the wall said and not how long the meat was on.
        var cook = new Cook
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            StartedAt = new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            FinishReason = CookFinishReason.Manual,
        };

        Assert.Equal(TimeSpan.FromHours(14), CookStatistics.For(cook).Duration);
    }

    [Fact]
    public void A_null_cook_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => CookStatistics.For(null!));
    }
}
