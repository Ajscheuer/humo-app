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
    public const string Unit_Litres_Short = nameof(Unit_Litres_Short);

    public const string Equipment_Title = nameof(Equipment_Title);
    public const string Equipment_Add = nameof(Equipment_Add);
    public const string Equipment_Edit = nameof(Equipment_Edit);
    public const string Equipment_Delete = nameof(Equipment_Delete);
    public const string Equipment_Name = nameof(Equipment_Name);
    public const string Equipment_Type = nameof(Equipment_Type);
    public const string Equipment_Insulation = nameof(Equipment_Insulation);
    public const string Equipment_FireboxVolume = nameof(Equipment_FireboxVolume);
    public const string Equipment_CookChamberVolume = nameof(Equipment_CookChamberVolume);
    public const string Equipment_Notes = nameof(Equipment_Notes);
    public const string Equipment_None = nameof(Equipment_None);
    public const string Equipment_NameRequired = nameof(Equipment_NameRequired);
    public const string Equipment_InUse = nameof(Equipment_InUse);
    public const string Equipment_Gone = nameof(Equipment_Gone);
    public const string StartCook_Equipment = nameof(StartCook_Equipment);

    public const string EquipmentType_Offset = nameof(EquipmentType_Offset);
    public const string EquipmentType_Kettle = nameof(EquipmentType_Kettle);
    public const string EquipmentType_Kamado = nameof(EquipmentType_Kamado);
    public const string EquipmentType_Wsm = nameof(EquipmentType_Wsm);
    public const string EquipmentType_Pellet = nameof(EquipmentType_Pellet);
    public const string EquipmentType_Parrilla = nameof(EquipmentType_Parrilla);

    public const string Insulation_None = nameof(Insulation_None);
    public const string Insulation_Light = nameof(Insulation_Light);
    public const string Insulation_Heavy = nameof(Insulation_Heavy);

    public const string Fuel_Add = nameof(Fuel_Add);
    public const string Fuel_Title = nameof(Fuel_Title);
    public const string Fuel_WoodType = nameof(Fuel_WoodType);
    public const string Fuel_Form = nameof(Fuel_Form);
    public const string Fuel_SizeClass = nameof(Fuel_SizeClass);
    public const string Fuel_Count = nameof(Fuel_Count);
    public const string Fuel_Weight = nameof(Fuel_Weight);
    public const string Fuel_LastAdded = nameof(Fuel_LastAdded);
    public const string Fuel_NeverFed = nameof(Fuel_NeverFed);
    public const string Fuel_MoreOptions = nameof(Fuel_MoreOptions);

    public const string WoodType_Oak = nameof(WoodType_Oak);
    public const string WoodType_PostOak = nameof(WoodType_PostOak);
    public const string WoodType_Hickory = nameof(WoodType_Hickory);
    public const string WoodType_Mesquite = nameof(WoodType_Mesquite);
    public const string WoodType_Pecan = nameof(WoodType_Pecan);
    public const string WoodType_Apple = nameof(WoodType_Apple);
    public const string WoodType_Cherry = nameof(WoodType_Cherry);
    public const string WoodType_Maple = nameof(WoodType_Maple);
    public const string WoodType_Quebracho = nameof(WoodType_Quebracho);
    public const string WoodType_Espinillo = nameof(WoodType_Espinillo);
    public const string WoodType_Other = nameof(WoodType_Other);

    public const string FuelForm_Split = nameof(FuelForm_Split);
    public const string FuelForm_Chunk = nameof(FuelForm_Chunk);
    public const string FuelForm_Charcoal = nameof(FuelForm_Charcoal);
    public const string FuelForm_Pellets = nameof(FuelForm_Pellets);

    public const string SizeClass_Small = nameof(SizeClass_Small);
    public const string SizeClass_Medium = nameof(SizeClass_Medium);
    public const string SizeClass_Large = nameof(SizeClass_Large);

    public const string Event_Log = nameof(Event_Log);
    public const string Event_Title = nameof(Event_Title);
    public const string Event_Type = nameof(Event_Type);
    public const string Event_Note = nameof(Event_Note);

    public const string EventType_Wrapped = nameof(EventType_Wrapped);
    public const string EventType_Spritzed = nameof(EventType_Spritzed);
    public const string EventType_Rested = nameof(EventType_Rested);
    public const string EventType_Other = nameof(EventType_Other);
}
