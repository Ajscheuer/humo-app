namespace Humo.Shared.Units;

/// <summary>
/// The unit a temperature is <em>displayed</em> in. Storage is always Celsius.
/// This is a user setting, deliberately independent of language: an American
/// cook with a Spanish phone still thinks in Fahrenheit.
/// </summary>
public enum TemperatureUnit
{
    Celsius = 0,
    Fahrenheit = 1,
}

/// <summary>
/// The unit a weight is <em>displayed</em> in. Storage is always kilograms.
/// </summary>
public enum WeightUnit
{
    Kilograms = 0,
    Pounds = 1,
}

/// <summary>
/// The unit a volume is <em>displayed</em> in. Storage is always litres.
/// Gallons are US gallons — Humo's first market is American BBQ, where smoker
/// capacity is quoted in US gallons.
/// </summary>
public enum VolumeUnit
{
    Litres = 0,
    UsGallons = 1,
}
