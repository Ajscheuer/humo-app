using Humo.Core.Settings;

namespace Humo.App.Services;

/// <summary>
/// <see cref="IAppPreferences"/> over the platform preference store. This is the
/// only place MAUI's <c>Preferences</c> API is touched.
/// </summary>
public sealed class MauiAppPreferences : IAppPreferences
{
    public string? GetString(string key)
        => Preferences.Default.ContainsKey(key) ? Preferences.Default.Get<string?>(key, null) : null;

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            Preferences.Default.Remove(key);
            return;
        }

        Preferences.Default.Set(key, value);
    }

    public void Remove(string key) => Preferences.Default.Remove(key);
}
