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

    public const string StartCook_Title = nameof(StartCook_Title);
    public const string StartCook_MeatType = nameof(StartCook_MeatType);
    public const string StartCook_Weight = nameof(StartCook_Weight);
    public const string StartCook_TargetTemp = nameof(StartCook_TargetTemp);
    public const string StartCook_Optional = nameof(StartCook_Optional);
    public const string StartCook_Start = nameof(StartCook_Start);
    public const string ActiveCook_Elapsed = nameof(ActiveCook_Elapsed);
    public const string ActiveCook_LastMeatTemp = nameof(ActiveCook_LastMeatTemp);
    public const string ActiveCook_LastPitTemp = nameof(ActiveCook_LastPitTemp);
    public const string ActiveCook_NoReadings = nameof(ActiveCook_NoReadings);
    public const string ActiveCook_LogTemp = nameof(ActiveCook_LogTemp);
    public const string ActiveCook_Finish = nameof(ActiveCook_Finish);
    public const string ActiveCook_NoActiveCook = nameof(ActiveCook_NoActiveCook);
    public const string LogTemp_Title = nameof(LogTemp_Title);
    public const string LogTemp_MeatTemp = nameof(LogTemp_MeatTemp);
    public const string LogTemp_PitTemp = nameof(LogTemp_PitTemp);
    public const string LogTemp_RecordedAt = nameof(LogTemp_RecordedAt);
    public const string LogTemp_Note = nameof(LogTemp_Note);
    public const string Cook_Finished = nameof(Cook_Finished);
    public const string Cook_Rating = nameof(Cook_Rating);
    public const string MeatType_Brisket = nameof(MeatType_Brisket);
    public const string MeatType_PorkButt = nameof(MeatType_PorkButt);
    public const string MeatType_PorkRibs = nameof(MeatType_PorkRibs);
    public const string MeatType_BeefRibs = nameof(MeatType_BeefRibs);
    public const string MeatType_Chicken = nameof(MeatType_Chicken);
    public const string MeatType_Turkey = nameof(MeatType_Turkey);
    public const string MeatType_PorkLoin = nameof(MeatType_PorkLoin);
    public const string MeatType_Lamb = nameof(MeatType_Lamb);
    public const string MeatType_Sausage = nameof(MeatType_Sausage);
    public const string MeatType_Other = nameof(MeatType_Other);

    public const string Unit_Celsius_Short = nameof(Unit_Celsius_Short);
    public const string Unit_Fahrenheit_Short = nameof(Unit_Fahrenheit_Short);
    public const string Unit_Kilograms_Short = nameof(Unit_Kilograms_Short);
    public const string Unit_Pounds_Short = nameof(Unit_Pounds_Short);
}
