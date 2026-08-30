using System.Globalization;
using Humo.Shared.Units;

namespace Humo.Core.Settings;

/// <summary>
/// The user's display preferences.
/// <para>
/// Language and unit are deliberately separate settings. An American cook whose
/// phone is in Spanish still wants °F, so choosing a language must never change
/// the temperature unit and vice versa.
/// </para>
/// </summary>
public interface IUserSettings
{
    /// <summary>
    /// In-app language override, or <c>null</c> to follow the device language.
    /// </summary>
    CultureInfo? LanguageOverride { get; set; }

    /// <summary>Unit temperatures are displayed in. Storage is always Celsius.</summary>
    TemperatureUnit TemperatureUnit { get; set; }

    /// <summary>Unit weights are displayed in. Storage is always kilograms.</summary>
    WeightUnit WeightUnit { get; set; }

    /// <summary>Raised when any setting changes, with the name of the changed setting.</summary>
    event EventHandler<string>? SettingChanged;
}
