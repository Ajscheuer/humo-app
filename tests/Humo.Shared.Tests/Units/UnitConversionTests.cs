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

    [Theory]
    [InlineData(1d, 2.20462262d)]
    [InlineData(0.5d, 1.10231131d)]
    public void PoundsToKilograms_is_the_exact_inverse_of_KilogramsToPounds(double kg, double lb)
    {
        Assert.Equal(kg, UnitConversion.PoundsToKilograms(lb), precision: 8);
    }

    [Theory]
    [InlineData(1d, 3.785411784d)]
    [InlineData(275d, 1040.988240600d)]  // a 275-gallon offset in litres
    public void UsGallonsToLitres_converts_known_volumes(double gallons, double expectedLitres)
    {
        Assert.Equal(expectedLitres, UnitConversion.UsGallonsToLitres(gallons), precision: 6);
        Assert.Equal(gallons, UnitConversion.LitresToUsGallons(expectedLitres), precision: 6);
    }

    [Fact]
    public void Sub_zero_ambient_temperatures_convert_correctly()
    {
        // Winter cooks are a real case, and a sign error here would only show up
        // in January.
        Assert.Equal(14d, UnitConversion.CelsiusToFahrenheit(-10d), precision: 9);
        Assert.Equal(-10d, UnitConversion.FahrenheitToCelsius(14d), precision: 9);
    }

    [Fact]
    public void Conversion_does_not_round()
    {
        // Rounding is a formatting concern. If it crept in here, every stored
        // Celsius value would drift a little each time a user edited an entry.
        var precise = UnitConversion.FahrenheitToCelsius(225d);

        Assert.NotEqual(Math.Round(precise, 2), precise);
        Assert.Equal(107.222222222222d, precise, precision: 10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_pass_through_rather_than_throwing(double value)
    {
        // Pins current behaviour deliberately: conversion is arithmetic and does
        // not validate. Rejecting a NaN reading is the job of entry validation,
        // which is where the user can be told about it. If that ever moves here,
        // this test should fail and be rewritten rather than deleted.
        Assert.Equal(double.IsNaN(value), double.IsNaN(UnitConversion.CelsiusToFahrenheit(value)));
        Assert.True(double.IsNaN(value) || double.IsInfinity(UnitConversion.CelsiusToFahrenheit(value)));
    }

    [Fact]
    public void Physically_impossible_temperatures_are_not_rejected_here()
    {
        // Below absolute zero. Same reasoning as above: validation belongs at the
        // point of entry, not in arithmetic. Recorded so the absence of a guard
        // is a decision rather than an oversight.
        Assert.Equal(-500d, UnitConversion.FahrenheitToCelsius(UnitConversion.CelsiusToFahrenheit(-500d)), precision: 9);
    }

    [Fact]
    public void An_unknown_unit_throws_rather_than_silently_returning_the_wrong_number()
    {
        const TemperatureUnit invalidTemperature = (TemperatureUnit)99;
        const WeightUnit invalidWeight = (WeightUnit)99;
        const VolumeUnit invalidVolume = (VolumeUnit)99;

        // Every conversion entry point, not just the first one: a switch that
        // silently returned its input would corrupt data with no error at all.
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromCelsius(100d, invalidTemperature));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.ToCelsius(100d, invalidTemperature));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.DeltaFromCelsius(10d, invalidTemperature));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromKilograms(1d, invalidWeight));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.ToKilograms(1d, invalidWeight));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromLitres(1d, invalidVolume));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.ToLitres(1d, invalidVolume));
    }

    [Fact]
    public void Zero_converts_as_a_delta_and_as_a_point_differently()
    {
        // 0 °C is 32 °F as a temperature; a 0 °C change is a 0 °F change.
        Assert.Equal(32d, UnitConversion.FromCelsius(0d, TemperatureUnit.Fahrenheit));
        Assert.Equal(0d, UnitConversion.DeltaFromCelsius(0d, TemperatureUnit.Fahrenheit));
    }
}
