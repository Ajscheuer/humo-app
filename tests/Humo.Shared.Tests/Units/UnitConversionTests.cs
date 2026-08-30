using Humo.Shared.Units;

namespace Humo.Shared.Tests.Units;

public class UnitConversionTests
{
    [Theory]
    [InlineData(0d, 32d)]
    [InlineData(100d, 212d)]
    [InlineData(-40d, -40d)]
    [InlineData(107.2222222222d, 225d)]  // 225 °F — the classic low-and-slow pit temp
    [InlineData(93.3333333333d, 200d)]   // 200 °F — brisket pulled temp
    public void CelsiusToFahrenheit_converts_known_points(double celsius, double expectedF)
    {
        Assert.Equal(expectedF, UnitConversion.CelsiusToFahrenheit(celsius), precision: 6);
    }

    [Theory]
    [InlineData(225d)]
    [InlineData(250d)]
    [InlineData(203d)]
    [InlineData(-4d)]
    public void Fahrenheit_survives_a_round_trip_through_Celsius_storage(double fahrenheit)
    {
        // This is the case that matters in practice: a user types 225 °F, Humo
        // stores Celsius, and the same 225 must come back out on screen.
        var stored = UnitConversion.ToCelsius(fahrenheit, TemperatureUnit.Fahrenheit);
        var displayed = UnitConversion.FromCelsius(stored, TemperatureUnit.Fahrenheit);

        Assert.Equal(fahrenheit, displayed, precision: 9);
    }

    [Fact]
    public void Celsius_passes_through_unchanged()
    {
        Assert.Equal(110d, UnitConversion.FromCelsius(110d, TemperatureUnit.Celsius));
        Assert.Equal(110d, UnitConversion.ToCelsius(110d, TemperatureUnit.Celsius));
    }

    [Fact]
    public void Temperature_deltas_do_not_apply_the_32_degree_offset()
    {
        // A 10 °C rise is an 18 °F rise, not 50 °F. Getting this wrong would
        // quietly corrupt the fire model's envelopes and stall detection.
        Assert.Equal(18d, UnitConversion.DeltaFromCelsius(10d, TemperatureUnit.Fahrenheit), precision: 9);
        Assert.Equal(10d, UnitConversion.DeltaFromCelsius(10d, TemperatureUnit.Celsius));
    }

    [Theory]
    [InlineData(1d, 2.2046226218d)]
    [InlineData(6.8d, 14.9914338284d)]  // a 15 lb packer brisket
    public void KilogramsToPounds_converts_known_weights(double kg, double expectedLb)
    {
        Assert.Equal(expectedLb, UnitConversion.KilogramsToPounds(kg), precision: 6);
    }

    [Theory]
    [InlineData(15d)]
    [InlineData(8.5d)]
    public void Pounds_survive_a_round_trip_through_kilogram_storage(double pounds)
    {
        var stored = UnitConversion.ToKilograms(pounds, WeightUnit.Pounds);
        var displayed = UnitConversion.FromKilograms(stored, WeightUnit.Pounds);

        Assert.Equal(pounds, displayed, precision: 9);
    }

    [Theory]
    [InlineData(275d)]  // a 275-gallon offset
    [InlineData(22d)]
    public void UsGallons_survive_a_round_trip_through_litre_storage(double gallons)
    {
        var stored = UnitConversion.ToLitres(gallons, VolumeUnit.UsGallons);
        var displayed = UnitConversion.FromLitres(stored, VolumeUnit.UsGallons);

        Assert.Equal(gallons, displayed, precision: 9);
    }

    [Fact]
    public void An_unknown_unit_throws_rather_than_silently_returning_the_wrong_number()
    {
        const TemperatureUnit invalid = (TemperatureUnit)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromCelsius(100d, invalid));
    }
}
