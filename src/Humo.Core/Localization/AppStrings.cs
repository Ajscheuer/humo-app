namespace Humo.Core.Localization;

/// <summary>
/// Compile-time-safe names of the resource keys in AppResources.resx.
/// <para>
/// ViewModels reference <c>AppStrings.Settings_Title</c> rather than the literal
/// <c>"Settings_Title"</c>, so a renamed key is a build error instead of a
/// missing string discovered by a user. <c>ResourceParityTests</c> asserts that
/// every constant here resolves in both the English and Spanish resources.
/// </para>
/// </summary>
public static class AppStrings
{
    public const string App_Name = nameof(App_Name);

    public const string Common_Cancel = nameof(Common_Cancel);
    public const string Common_Save = nameof(Common_Save);
    public const string Common_Done = nameof(Common_Done);

    public const string Settings_Title = nameof(Settings_Title);
    public const string Settings_Language = nameof(Settings_Language);
    public const string Settings_Language_System = nameof(Settings_Language_System);
    public const string Settings_Language_English = nameof(Settings_Language_English);
    public const string Settings_Language_Spanish = nameof(Settings_Language_Spanish);
    public const string Settings_TemperatureUnit = nameof(Settings_TemperatureUnit);
    public const string Settings_TemperatureUnit_Explanation = nameof(Settings_TemperatureUnit_Explanation);

    public const string Unit_Celsius_Short = nameof(Unit_Celsius_Short);
    public const string Unit_Fahrenheit_Short = nameof(Unit_Fahrenheit_Short);
    public const string Unit_Kilograms_Short = nameof(Unit_Kilograms_Short);
    public const string Unit_Pounds_Short = nameof(Unit_Pounds_Short);
}
