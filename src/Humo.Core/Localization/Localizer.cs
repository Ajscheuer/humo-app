using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Humo.Core.Localization;

/// <summary>
/// Default <see cref="ILocalizer"/>, backed by the AppResources resource set.
/// <para>
/// Culture resolution order is: in-app override → device culture → English.
/// The override is supplied by the caller (it is persisted in preferences by
/// the app); this class only applies what it is given, so it stays testable
/// without any storage.
/// </para>
/// </summary>
public sealed class Localizer : ILocalizer, INotifyPropertyChanged
{
    /// <summary>The neutral fallback culture. English strings live in AppResources.resx.</summary>
    public static readonly CultureInfo FallbackCulture = new("en");

    /// <summary>Cultures Humo ships strings for. Both are launch languages.</summary>
    public static readonly IReadOnlyList<CultureInfo> SupportedCultures =
    [
        new CultureInfo("en"),
        new CultureInfo("es"),
    ];

    private readonly ResourceManager _resources;
    private CultureInfo _currentCulture;

    public Localizer()
        : this(new ResourceManager("Humo.Core.Resources.Strings.AppResources", typeof(Localizer).Assembly))
    {
    }

    internal Localizer(ResourceManager resources)
    {
        _resources = resources;
        _currentCulture = FallbackCulture;
    }

    public CultureInfo CurrentCulture => _currentCulture;

    public event EventHandler? CultureChanged;

    /// <summary>
    /// Raised for the indexer when the culture changes. XAML binds through
    /// <c>{loc:Translate}</c>, which is an indexer binding onto this object, so
    /// this is what makes an in-app language switch re-resolve every visible
    /// string without rebuilding the page.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // A missing string returns its key rather than throwing or returning
        // null: a screen showing "Cook_Start" is an obvious, reportable bug,
        // while a crash or a blank label in front of a user is not.
        return _resources.GetString(key, _currentCulture) ?? key;
    }

    public string Format(string key, params object?[] args)
        => string.Format(_currentCulture, Get(key), args);

    public void SetCulture(CultureInfo? culture)
    {
        var resolved = Resolve(culture);
        if (resolved.Name == _currentCulture.Name)
        {
            return;
        }

        _currentCulture = resolved;

        // Dates and numbers follow the language; the temperature unit does not,
        // and is applied separately by a display converter.
        CultureInfo.CurrentCulture = resolved;
        CultureInfo.CurrentUICulture = resolved;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Maps a requested culture onto one Humo actually ships strings for.
    /// <c>es-AR</c> and <c>es-MX</c> both resolve to <c>es</c>; anything
    /// unsupported falls back to English.
    /// </summary>
    public static CultureInfo Resolve(CultureInfo? requested)
    {
        var candidate = requested ?? CultureInfo.CurrentUICulture;

        foreach (var supported in SupportedCultures)
        {
            if (candidate.TwoLetterISOLanguageName.Equals(
                    supported.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return FallbackCulture;
    }
}
