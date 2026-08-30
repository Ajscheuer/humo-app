using System.Globalization;
using Humo.Core.Localization;
using Humo.Shared.Units;

namespace Humo.Core.Settings;

/// <summary>
/// <see cref="IUserSettings"/> backed by <see cref="IAppPreferences"/>.
/// </summary>
public sealed class UserSettings : IUserSettings
{
    internal const string LanguageOverrideKey = "settings.language_override";
    internal const string TemperatureUnitKey = "settings.temperature_unit";
    internal const string WeightUnitKey = "settings.weight_unit";

    private readonly IAppPreferences _preferences;

    public UserSettings(IAppPreferences preferences)
    {
        _preferences = preferences;
    }

    public event EventHandler<string>? SettingChanged;

    public CultureInfo? LanguageOverride
    {
        get
        {
            var stored = _preferences.GetString(LanguageOverrideKey);
            if (string.IsNullOrWhiteSpace(stored))
            {
                return null;
            }

            try
            {
                return new CultureInfo(stored);
            }
            catch (CultureNotFoundException)
            {
                // A stored culture the platform no longer recognises should not
                // brick the app; fall back to following the device.
                return null;
            }
        }

        set
        {
            if (value is null)
            {
                _preferences.Remove(LanguageOverrideKey);
            }
            else
            {
                _preferences.SetString(LanguageOverrideKey, Localizer.Resolve(value).Name);
            }

            SettingChanged?.Invoke(this, nameof(LanguageOverride));
        }
    }

    public TemperatureUnit TemperatureUnit
    {
        get => ReadEnum(TemperatureUnitKey, TemperatureUnit.Celsius);
        set
        {
            _preferences.SetString(TemperatureUnitKey, value.ToString());
            SettingChanged?.Invoke(this, nameof(TemperatureUnit));
        }
    }

    public WeightUnit WeightUnit
    {
        get => ReadEnum(WeightUnitKey, WeightUnit.Kilograms);
        set
        {
            _preferences.SetString(WeightUnitKey, value.ToString());
            SettingChanged?.Invoke(this, nameof(WeightUnit));
        }
    }

    private TEnum ReadEnum<TEnum>(string key, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(_preferences.GetString(key), ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
}
