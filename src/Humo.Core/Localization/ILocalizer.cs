using System.Globalization;

namespace Humo.Core.Localization;

/// <summary>
/// Resolves user-facing strings. Every ViewModel that needs text takes this
/// dependency rather than touching resources statically, which keeps ViewModels
/// testable and lets tests assert <em>which key</em> was used rather than which
/// English words came out.
/// </summary>
public interface ILocalizer
{
    /// <summary>The culture currently used to resolve strings.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Raised when <see cref="CurrentCulture"/> changes, so bound UI can re-resolve.</summary>
    event EventHandler? CultureChanged;

    /// <summary>Resolves a key. Returns the key itself if it is missing, never null.</summary>
    string this[string key] { get; }

    /// <summary>Resolves a key. Returns the key itself if it is missing, never null.</summary>
    string Get(string key);

    /// <summary>Resolves a key and formats it with <paramref name="args"/> using the current culture.</summary>
    string Format(string key, params object?[] args);

    /// <summary>
    /// Applies a language. Pass <c>null</c> to clear the in-app override and fall
    /// back to the device language.
    /// </summary>
    void SetCulture(CultureInfo? culture);
}
