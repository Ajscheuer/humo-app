using Humo.Core.Settings;

namespace Humo.Core.Tests.Support;

/// <summary>
/// <see cref="IAppPreferences"/> in a dictionary. Two instances over the same
/// dictionary stand in for two launches of the app on one device.
/// </summary>
internal sealed class InMemoryPreferences : IAppPreferences
{
    private readonly Dictionary<string, string> _values = [];

    public string? GetString(string key) => _values.GetValueOrDefault(key);

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }

        _values[key] = value;
    }

    public void Remove(string key) => _values.Remove(key);
}
