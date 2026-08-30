using System.Globalization;
using Humo.Core.Settings;
using Humo.Shared.Units;

namespace Humo.Core.Tests.Settings;

public class UserSettingsTests
{
    private readonly InMemoryPreferences _preferences = new();

    [Fact]
    public void Language_defaults_to_following_the_device()
    {
        var settings = new UserSettings(_preferences);

        Assert.Null(settings.LanguageOverride);
    }

    [Fact]
    public void Language_override_round_trips_and_is_stored_as_a_supported_culture()
    {
        var settings = new UserSettings(_preferences)
        {
            LanguageOverride = new CultureInfo("es-AR"),
        };

        Assert.Equal("es", settings.LanguageOverride?.Name);
    }

    [Fact]
    public void Clearing_the_language_override_returns_to_following_the_device()
    {
        var settings = new UserSettings(_preferences)
        {
            LanguageOverride = new CultureInfo("es"),
        };

        settings.LanguageOverride = null;

        Assert.Null(settings.LanguageOverride);
    }

    [Fact]
    public void A_stored_culture_the_platform_no_longer_recognises_does_not_throw()
    {
        _preferences.SetString(UserSettings.LanguageOverrideKey, "not-a-culture-!!");
        var settings = new UserSettings(_preferences);

        Assert.Null(settings.LanguageOverride);
    }

    [Fact]
    public void Choosing_Spanish_does_not_change_the_temperature_unit()
    {
        // The whole point of keeping these settings separate: an American cook
        // with a Spanish phone still reads °F.
        var settings = new UserSettings(_preferences)
        {
            TemperatureUnit = TemperatureUnit.Fahrenheit,
        };

        settings.LanguageOverride = new CultureInfo("es");

        Assert.Equal(TemperatureUnit.Fahrenheit, settings.TemperatureUnit);
    }

    [Fact]
    public void Unit_settings_round_trip()
    {
        var settings = new UserSettings(_preferences)
        {
            TemperatureUnit = TemperatureUnit.Fahrenheit,
            WeightUnit = WeightUnit.Pounds,
        };

        var reloaded = new UserSettings(_preferences);

        Assert.Equal(TemperatureUnit.Fahrenheit, reloaded.TemperatureUnit);
        Assert.Equal(WeightUnit.Pounds, reloaded.WeightUnit);
    }

    [Fact]
    public void Changing_a_setting_reports_which_one_changed()
    {
        var settings = new UserSettings(_preferences);
        var changed = new List<string>();
        settings.SettingChanged += (_, name) => changed.Add(name);

        settings.TemperatureUnit = TemperatureUnit.Fahrenheit;
        settings.LanguageOverride = new CultureInfo("es");

        Assert.Equal([nameof(IUserSettings.TemperatureUnit), nameof(IUserSettings.LanguageOverride)], changed);
    }

    [Fact]
    public void Clearing_the_language_override_reports_the_change()
    {
        var settings = new UserSettings(_preferences) { LanguageOverride = new CultureInfo("es") };
        var changed = new List<string>();
        settings.SettingChanged += (_, name) => changed.Add(name);

        settings.LanguageOverride = null;

        Assert.Equal([nameof(IUserSettings.LanguageOverride)], changed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_stored_language_means_follow_the_device(string stored)
    {
        _preferences.SetString(UserSettings.LanguageOverrideKey, stored);
        var settings = new UserSettings(_preferences);

        Assert.Null(settings.LanguageOverride);
    }

    [Fact]
    public void An_unrecognised_stored_unit_falls_back_to_the_default()
    {
        // A value written by a future version, or corrupted storage, must not
        // throw on a screen the user is looking at.
        _preferences.SetString(UserSettings.TemperatureUnitKey, "Kelvin");
        _preferences.SetString(UserSettings.WeightUnitKey, "stones");
        var settings = new UserSettings(_preferences);

        Assert.Equal(TemperatureUnit.Celsius, settings.TemperatureUnit);
        Assert.Equal(WeightUnit.Kilograms, settings.WeightUnit);
    }

    [Fact]
    public void A_stored_unit_is_read_case_insensitively()
    {
        _preferences.SetString(UserSettings.TemperatureUnitKey, "fahrenheit");
        var settings = new UserSettings(_preferences);

        Assert.Equal(TemperatureUnit.Fahrenheit, settings.TemperatureUnit);
    }

    [Fact]
    public void Units_are_stored_by_name_so_reordering_the_enum_cannot_change_a_users_setting()
    {
        var settings = new UserSettings(_preferences) { TemperatureUnit = TemperatureUnit.Fahrenheit };

        Assert.Equal("Fahrenheit", _preferences.GetString(UserSettings.TemperatureUnitKey));
    }

    [Fact]
    public void Setting_the_same_value_twice_still_reports_it()
    {
        // Pins current behaviour: the setter does not compare before writing.
        // Cheap, and it means a caller can rely on the event firing.
        var settings = new UserSettings(_preferences);
        var changed = new List<string>();
        settings.SettingChanged += (_, name) => changed.Add(name);

        settings.TemperatureUnit = TemperatureUnit.Fahrenheit;
        settings.TemperatureUnit = TemperatureUnit.Fahrenheit;

        Assert.Equal(2, changed.Count);
    }

    [Fact]
    public void An_unsupported_language_override_is_stored_as_the_language_actually_shipped()
    {
        // German is not a launch language; storing "de" would leave the app
        // permanently resolving through a fallback it cannot show a name for.
        var settings = new UserSettings(_preferences) { LanguageOverride = new CultureInfo("de-DE") };

        Assert.Equal("en", settings.LanguageOverride?.Name);
    }

    private sealed class InMemoryPreferences : IAppPreferences
    {
        private readonly Dictionary<string, string> _values = [];

        public string? GetString(string key) => _values.GetValueOrDefault(key);

        public void SetString(string key, string? value)
        {
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
        }

        public void Remove(string key) => _values.Remove(key);
    }
}
