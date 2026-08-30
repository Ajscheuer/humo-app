namespace Humo.Shared.Units;

/// <summary>
/// The single place unit conversion happens in Humo.
/// <para>
/// Storage is Celsius, kilograms and litres — everywhere, on device, in the API,
/// in the database, and in cached analytics. These helpers exist to convert at
/// the display boundary and nowhere else. Nothing in this class rounds: rounding
/// is a formatting concern and belongs in the converter that renders the value.
/// </para>
/// </summary>
public static class UnitConversion
{
    private const double LitresPerUsGallon = 3.785411784;

    // ---- Temperature -------------------------------------------------------

    public static double CelsiusToFahrenheit(double celsius) => (celsius * 9d / 5d) + 32d;

    public static double FahrenheitToCelsius(double fahrenheit) => (fahrenheit - 32d) * 5d / 9d;

    /// <summary>Converts a stored Celsius value into the unit the user reads in.</summary>
    public static double FromCelsius(double celsius, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Celsius => celsius,
        TemperatureUnit.Fahrenheit => CelsiusToFahrenheit(celsius),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Converts a value the user typed, in their unit, into Celsius for storage.</summary>
    public static double ToCelsius(double value, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Celsius => value,
        TemperatureUnit.Fahrenheit => FahrenheitToCelsius(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>
    /// Converts a temperature <em>difference</em> (not a point on the scale).
    /// A 10 °C rise is an 18 °F rise, not 50 °F — the offset does not apply.
    /// Used by the fire model's envelopes and by stall detection.
    /// </summary>
    public static double DeltaFromCelsius(double celsiusDelta, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Celsius => celsiusDelta,
        TemperatureUnit.Fahrenheit => celsiusDelta * 9d / 5d,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    // ---- Weight ------------------------------------------------------------

    public static double KilogramsToPounds(double kilograms) => kilograms / 0.45359237d;

    public static double PoundsToKilograms(double pounds) => pounds * 0.45359237d;

    public static double FromKilograms(double kilograms, WeightUnit unit) => unit switch
    {
        WeightUnit.Kilograms => kilograms,
        WeightUnit.Pounds => KilogramsToPounds(kilograms),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    public static double ToKilograms(double value, WeightUnit unit) => unit switch
    {
        WeightUnit.Kilograms => value,
        WeightUnit.Pounds => PoundsToKilograms(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    // ---- Volume ------------------------------------------------------------

    public static double LitresToUsGallons(double litres) => litres / LitresPerUsGallon;

    public static double UsGallonsToLitres(double gallons) => gallons * LitresPerUsGallon;

    public static double FromLitres(double litres, VolumeUnit unit) => unit switch
    {
        VolumeUnit.Litres => litres,
        VolumeUnit.UsGallons => LitresToUsGallons(litres),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    public static double ToLitres(double value, VolumeUnit unit) => unit switch
    {
        VolumeUnit.Litres => value,
        VolumeUnit.UsGallons => UsGallonsToLitres(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };
}
