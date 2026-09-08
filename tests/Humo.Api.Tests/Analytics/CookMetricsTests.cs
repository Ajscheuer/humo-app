using Humo.Api.Analytics;

namespace Humo.Api.Tests.Analytics;

public class StallDetectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_brisket_that_plateaus_reports_the_plateau()
    {
        // Climbs to the band, sits at 68 for four hours, then climbs out.
        var readings = new List<Reading>
        {
            At(0, 40), At(1, 55), At(2, 66),
            At(3, 67), At(4, 67.5), At(5, 68), At(6, 68.2),
            At(7, 74), At(8, 90),
        };

        var stall = CookMetrics.FindStall(readings);

        Assert.NotNull(stall);
        Assert.Equal(Start.AddHours(2), stall.Value.StartedAt);
        Assert.Equal(Start.AddHours(6), stall.Value.EndedAt);
        Assert.Equal(TimeSpan.FromHours(4), stall.Value.Duration);
    }

    [Fact]
    public void A_cook_that_climbs_throughout_has_no_stall()
    {
        var readings = new List<Reading> { At(0, 20), At(1, 40), At(2, 60), At(3, 78), At(4, 95) };

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void A_flat_run_outside_the_plateau_band_is_not_a_stall()
    {
        // Meat sitting at 30 °C for hours is a cook that has not started, or a
        // fire that went out. Neither is the stall.
        var readings = new List<Reading> { At(0, 30), At(2, 30.5), At(4, 31), At(6, 31.2) };

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void A_flat_run_above_the_band_is_not_a_stall()
    {
        // Resting at 92 °C is not a stall either.
        var readings = new List<Reading> { At(0, 92), At(2, 92.2), At(4, 92.5) };

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void A_brief_plateau_is_not_reported()
    {
        // Twenty minutes of matching readings is noise.
        var readings = new List<Reading>
        {
            new(Start, 66), new(Start.AddMinutes(20), 66.1), new(Start.AddHours(2), 80),
        };

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void The_longest_plateau_wins_when_there_are_two()
    {
        var readings = new List<Reading>
        {
            At(0, 62), At(1, 62.5),          // one hour, flat
            At(2, 70),                        // climbs out, well above the rate
            At(3, 74), At(4, 74.2), At(5, 74.4), At(6, 74.5), // three hours, flat
            At(7, 85),
        };

        var stall = CookMetrics.FindStall(readings);

        Assert.NotNull(stall);
        Assert.Equal(TimeSpan.FromHours(3), stall.Value.Duration);
    }

    [Fact]
    public void Readings_logged_out_of_order_still_find_the_stall()
    {
        // recordedAt is editable, so a cook correcting a late-logged reading
        // genuinely produces this. Comparing in arrival order would measure
        // nonsense.
        var readings = new List<Reading> { At(6, 68.2), At(2, 66), At(4, 67.5), At(0, 40), At(8, 90) };

        var stall = CookMetrics.FindStall(readings);

        Assert.NotNull(stall);
        Assert.Equal(Start.AddHours(2), stall.Value.StartedAt);
    }

    [Fact]
    public void Two_readings_at_the_same_instant_are_not_a_stall()
    {
        // A duplicate timestamp would otherwise divide by zero and compare as
        // "not greater than the threshold", which reads as a stall.
        var readings = new List<Reading> { new(Start, 66), new(Start, 66), new(Start.AddHours(3), 90) };

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void A_falling_temperature_inside_the_band_counts_as_stalled()
    {
        // Wrapped and the temperature dips. It is not rising, which is what the
        // spec's "rises less than a threshold rate" means.
        var readings = new List<Reading> { At(0, 70), At(2, 69), At(4, 68.5), At(6, 85) };

        Assert.NotNull(CookMetrics.FindStall(readings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_cook_with_almost_no_readings_has_no_stall(int count)
    {
        var readings = Enumerable.Range(0, count).Select(i => At(i, 68)).ToList();

        Assert.Null(CookMetrics.FindStall(readings));
    }

    [Fact]
    public void A_cook_with_no_readings_at_all_does_not_throw()
    {
        Assert.Null(CookMetrics.FindStall([]));
    }

    private static Reading At(double hours, double tempC)
        => new(Start.AddHours(hours), tempC);
}

public class PitStabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_rock_steady_fire_scores_near_a_hundred()
    {
        var score = CookMetrics.PitStabilityScore(Readings(110, 110, 110, 110, 110, 110));

        Assert.Equal(100, score);
    }

    [Fact]
    public void A_wandering_fire_scores_lower_than_a_steady_one()
    {
        var steady = CookMetrics.PitStabilityScore(Readings(110, 112, 108, 111, 109, 110));
        var wandering = CookMetrics.PitStabilityScore(Readings(90, 140, 95, 150, 100, 130));

        Assert.NotNull(steady);
        Assert.NotNull(wandering);
        Assert.True(wandering < steady, $"Expected {wandering} to be worse than {steady}.");
    }

    [Fact]
    public void The_same_wobble_scores_better_on_a_hotter_fire()
    {
        // A 10 °C swing holding 250 °C on a parrilla is tighter control than the
        // same swing holding 110 °C on a smoker. An absolute measure would call
        // them identical.
        var smoker = CookMetrics.PitStabilityScore(Readings(100, 120, 100, 120, 100, 120));
        var parrilla = CookMetrics.PitStabilityScore(Readings(240, 260, 240, 260, 240, 260));

        Assert.True(parrilla > smoker, $"Expected {parrilla} to beat {smoker}.");
    }

    [Fact]
    public void One_lid_open_reading_does_not_dominate_the_score()
    {
        // Deviation is measured around the median and not squared, so a single
        // outlier costs a steady fire a little rather than defining it.
        var withOutlier = CookMetrics.PitStabilityScore(
            Readings(110, 110, 110, 110, 110, 110, 110, 110, 110, 40));

        Assert.NotNull(withOutlier);
        Assert.True(withOutlier > 90, $"One outlier should not gut the score; got {withOutlier}.");
    }

    [Fact]
    public void Too_few_readings_produce_no_score_rather_than_a_perfect_one()
    {
        Assert.Null(CookMetrics.PitStabilityScore(Readings(110, 110)));
    }

    [Fact]
    public void No_readings_at_all_produce_no_score()
    {
        // data-model.md open question 4: a cook can exist with no pit data. The
        // honest answer is "no score", not zero and not a hundred.
        Assert.Null(CookMetrics.PitStabilityScore([]));
    }

    [Fact]
    public void A_score_never_falls_below_zero()
    {
        var score = CookMetrics.PitStabilityScore(Readings(10, 400, 5, 380, 8, 420));

        Assert.NotNull(score);
        Assert.InRange(score.Value, 0, 100);
    }

    [Fact]
    public void A_freezing_pit_produces_no_score()
    {
        // A median at or below 0 °C is not a fire, and dividing by it inverts
        // the score.
        Assert.Null(CookMetrics.PitStabilityScore(Readings(-5, -5, -5, -5, -5, -5)));
    }

    private static List<Reading> Readings(params double[] temps)
        => temps.Select((t, i) => new Reading(Start.AddMinutes(i * 15), t)).ToList();
}

public class FuelEfficiencyTests
{
    [Fact]
    public void Fewer_loads_for_the_same_fire_is_more_efficient()
    {
        var frugal = CookMetrics.FuelEfficiency(6, TimeSpan.FromHours(12), 110, 20);
        var hungry = CookMetrics.FuelEfficiency(24, TimeSpan.FromHours(12), 110, 20);

        Assert.NotNull(frugal);
        Assert.NotNull(hungry);
        Assert.True(frugal < hungry, "Lower is more efficient.");
    }

    [Fact]
    public void A_cold_day_does_not_read_as_poor_technique()
    {
        // The same cadence holding the same pit temperature, one in winter.
        var freezing = CookMetrics.FuelEfficiency(12, TimeSpan.FromHours(12), 110, -5);
        var mild = CookMetrics.FuelEfficiency(12, TimeSpan.FromHours(12), 110, 25);

        // product-spec.md 6: normalized by ambient so a cold windy day does not
        // look like poor technique. The cold cook is doing more work per load.
        Assert.NotNull(freezing);
        Assert.NotNull(mild);
        Assert.True(freezing < mild, $"Cold day {freezing} should score better than mild {mild}.");
    }

    [Fact]
    public void A_missing_ambient_is_assumed_mild_rather_than_refused()
    {
        var assumed = CookMetrics.FuelEfficiency(10, TimeSpan.FromHours(10), 110, averageAmbientTempC: null);
        var stated = CookMetrics.FuelEfficiency(10, TimeSpan.FromHours(10), 110, 20);

        // Ambient is optional and usually absent. Refusing to score would mean
        // most cooks have no fuel efficiency at all.
        Assert.Equal(stated, assumed);
    }

    [Fact]
    public void A_cook_with_no_pit_data_has_no_efficiency()
    {
        Assert.Null(CookMetrics.FuelEfficiency(10, TimeSpan.FromHours(10), averagePitTempC: null, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_cook_with_no_duration_has_no_efficiency(double hours)
    {
        Assert.Null(CookMetrics.FuelEfficiency(4, TimeSpan.FromHours(hours), 110, 20));
    }

    [Fact]
    public void A_pit_no_warmer_than_the_air_has_no_efficiency()
    {
        // Nothing was being held, so there is nothing to score. Without this the
        // divisor goes to zero or negative and the number inverts.
        Assert.Null(CookMetrics.FuelEfficiency(4, TimeSpan.FromHours(8), 20, 25));
    }

    [Fact]
    public void A_cook_with_no_fuel_logged_scores_zero_rather_than_nothing()
    {
        // A kamado run on one load genuinely has no fuel events after the first.
        // That is excellent efficiency, not missing data.
        Assert.Equal(0, CookMetrics.FuelEfficiency(0, TimeSpan.FromHours(10), 110, 20));
    }
}

public class TimePerKgTests
{
    [Fact]
    public void Time_per_kilo_is_hours_over_weight()
    {
        Assert.Equal(2, CookMetrics.TimePerKg(TimeSpan.FromHours(12), 6));
    }

    [Fact]
    public void A_running_cook_has_no_time_per_kilo()
    {
        Assert.Null(CookMetrics.TimePerKg(null, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void A_cook_with_no_weight_has_no_time_per_kilo(double weightKg)
    {
        Assert.Null(CookMetrics.TimePerKg(TimeSpan.FromHours(12), weightKg));
    }

    [Fact]
    public void A_negative_duration_yields_nothing_rather_than_a_negative_rate()
    {
        // A finish time before the start, which slice 3 already decided yields
        // nothing rather than a clamped zero.
        Assert.Null(CookMetrics.TimePerKg(TimeSpan.FromHours(-3), 6));
    }
}
