using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Humo.Core.Localization;
using Humo.Core.Settings;
using Humo.Shared.Units;

namespace Humo.Core.ViewModels;

/// <summary>
/// A language the user can pick, or "follow the device" when <see cref="Culture"/>
/// is null.
/// <para>
/// Declared top-level rather than nested inside the ViewModel so XAML can name it
/// as an <c>x:DataType</c> — compiled bindings cannot reference a nested type.
/// </para>
/// </summary>
public sealed record LanguageOption(CultureInfo? Culture, string DisplayNameKey);

/// <summary>
/// Language and unit settings.
/// <para>
/// This is the slice-0 ViewModel: it exists to prove the two settings are
/// genuinely independent and that switching language re-resolves strings at
/// runtime. It knows nothing about MAUI, which is why it is testable here.
/// </para>
/// </summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly IUserSettings _settings;

    public AppSettingsViewModel(ILocalizer localizer, IUserSettings settings)
    {
        _localizer = localizer;
        _settings = settings;

        LanguageOptions =
        [
            new LanguageOption(null, AppStrings.Settings_Language_System),
            new LanguageOption(new CultureInfo("en"), AppStrings.Settings_Language_English),
            new LanguageOption(new CultureInfo("es"), AppStrings.Settings_Language_Spanish),
        ];

        _selectedLanguage = LanguageOptions.FirstOrDefault(
            option => option.Culture?.Name == settings.LanguageOverride?.Name)
            ?? LanguageOptions[0];

        _useFahrenheit = settings.TemperatureUnit == TemperatureUnit.Fahrenheit;
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Title of the settings screen, resolved in the current language.</summary>
    public string Title => _localizer[AppStrings.Settings_Title];

    /// <summary>Explains that the temperature unit does not follow the language.</summary>
    public string TemperatureUnitExplanation => _localizer[AppStrings.Settings_TemperatureUnit_Explanation];

    /// <summary>The symbol currently shown next to temperatures.</summary>
    public string TemperatureUnitSymbol => _localizer[
        UseFahrenheit ? AppStrings.Unit_Fahrenheit_Short : AppStrings.Unit_Celsius_Short];

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _useFahrenheit;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        _settings.LanguageOverride = value.Culture;

        // A null override means "follow the device"; Resolve falls back to the
        // device culture, then to English.
        _localizer.SetCulture(value.Culture ?? CultureInfo.CurrentUICulture);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TemperatureUnitExplanation));
        OnPropertyChanged(nameof(TemperatureUnitSymbol));
    }

    partial void OnUseFahrenheitChanged(bool value)
    {
        // Note what does NOT happen here: changing the unit never touches the
        // language, and OnSelectedLanguageChanged never touches the unit.
        _settings.TemperatureUnit = value ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;

        OnPropertyChanged(nameof(TemperatureUnitSymbol));
    }

}
