namespace Humo.Core.Settings;

/// <summary>
/// Key/value storage for small user settings. Implemented in Humo.App over the
/// platform preference store; this abstraction is what keeps Humo.Core free of
/// MAUI types and unit-testable with an in-memory fake.
/// </summary>
public interface IAppPreferences
{
    string? GetString(string key);

    void SetString(string key, string? value);

    void Remove(string key);
}
