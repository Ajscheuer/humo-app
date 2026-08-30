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
