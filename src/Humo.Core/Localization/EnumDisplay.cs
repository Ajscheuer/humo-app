using Humo.Shared.Enums;

namespace Humo.Core.Localization;

/// <summary>
/// Maps stored enum values onto the resource keys that display them.
/// <para>
/// This mapping is why enums are stored as values rather than as text: the same
/// record reads correctly in English and in Spanish, and changing language does
/// not require touching data. Nothing here returns display text — it returns a
/// key, which <see cref="ILocalizer"/> resolves in the current language.
/// </para>
/// </summary>
public static class EnumDisplay
{
    private static readonly Dictionary<MeatType, string> MeatTypeKeys = new()
    {
        [MeatType.Brisket] = AppStrings.MeatType_Brisket,
        [MeatType.PorkButt] = AppStrings.MeatType_PorkButt,
        [MeatType.PorkRibs] = AppStrings.MeatType_PorkRibs,
        [MeatType.BeefRibs] = AppStrings.MeatType_BeefRibs,
        [MeatType.Chicken] = AppStrings.MeatType_Chicken,
        [MeatType.Turkey] = AppStrings.MeatType_Turkey,
        [MeatType.PorkLoin] = AppStrings.MeatType_PorkLoin,
        [MeatType.Lamb] = AppStrings.MeatType_Lamb,
        [MeatType.Sausage] = AppStrings.MeatType_Sausage,
        [MeatType.Other] = AppStrings.MeatType_Other,
    };

    /// <summary>
    /// The resource key for a meat type. An unmapped value — a newly added enum
    /// member whose string has not landed yet — falls back to "Other" rather
    /// than throwing, because a picker that crashes is worse than one showing a
    /// slightly wrong label.
    /// </summary>
    public static string KeyFor(MeatType meatType)
        => MeatTypeKeys.TryGetValue(meatType, out var key) ? key : AppStrings.MeatType_Other;

    /// <summary>Every meat type, in the order the picker should show them.</summary>
    public static IReadOnlyList<MeatType> MeatTypesInDisplayOrder { get; } =
    [
        // Roughly by how often a long-cook BBQ user reaches for them, with the
        // escape hatch last.
        MeatType.Brisket,
        MeatType.PorkButt,
        MeatType.PorkRibs,
        MeatType.BeefRibs,
        MeatType.Chicken,
        MeatType.Turkey,
        MeatType.PorkLoin,
        MeatType.Lamb,
        MeatType.Sausage,
        MeatType.Other,
    ];

    private static readonly Dictionary<EquipmentType, string> EquipmentTypeKeys = new()
    {
        [EquipmentType.Offset] = AppStrings.EquipmentType_Offset,
        [EquipmentType.Kettle] = AppStrings.EquipmentType_Kettle,
        [EquipmentType.Kamado] = AppStrings.EquipmentType_Kamado,
        [EquipmentType.Wsm] = AppStrings.EquipmentType_Wsm,
        [EquipmentType.Pellet] = AppStrings.EquipmentType_Pellet,
        [EquipmentType.Parrilla] = AppStrings.EquipmentType_Parrilla,
    };

    public static string KeyFor(EquipmentType type)
        => EquipmentTypeKeys.TryGetValue(type, out var key) ? key : AppStrings.EquipmentType_Offset;

    public static IReadOnlyList<EquipmentType> EquipmentTypesInDisplayOrder { get; } =
    [
        EquipmentType.Offset,
        EquipmentType.Kettle,
        EquipmentType.Kamado,
        EquipmentType.Wsm,
        EquipmentType.Pellet,
        EquipmentType.Parrilla,
    ];

    private static readonly Dictionary<InsulationLevel, string> InsulationKeys = new()
    {
        [InsulationLevel.None] = AppStrings.Insulation_None,
        [InsulationLevel.Light] = AppStrings.Insulation_Light,
        [InsulationLevel.Heavy] = AppStrings.Insulation_Heavy,
    };

    public static string KeyFor(InsulationLevel level)
        => InsulationKeys.TryGetValue(level, out var key) ? key : AppStrings.Insulation_None;

    public static IReadOnlyList<InsulationLevel> InsulationLevelsInDisplayOrder { get; } =
    [
        InsulationLevel.None,
        InsulationLevel.Light,
        InsulationLevel.Heavy,
    ];

    private static readonly Dictionary<WoodType, string> WoodTypeKeys = new()
    {
        [WoodType.Oak] = AppStrings.WoodType_Oak,
        [WoodType.PostOak] = AppStrings.WoodType_PostOak,
        [WoodType.Hickory] = AppStrings.WoodType_Hickory,
        [WoodType.Mesquite] = AppStrings.WoodType_Mesquite,
        [WoodType.Pecan] = AppStrings.WoodType_Pecan,
        [WoodType.Apple] = AppStrings.WoodType_Apple,
        [WoodType.Cherry] = AppStrings.WoodType_Cherry,
        [WoodType.Maple] = AppStrings.WoodType_Maple,
        [WoodType.Quebracho] = AppStrings.WoodType_Quebracho,
        [WoodType.Espinillo] = AppStrings.WoodType_Espinillo,
        [WoodType.Other] = AppStrings.WoodType_Other,
    };

    public static string KeyFor(WoodType type)
        => WoodTypeKeys.TryGetValue(type, out var key) ? key : AppStrings.WoodType_Other;

    public static IReadOnlyList<WoodType> WoodTypesInDisplayOrder { get; } =
    [
        WoodType.Oak,
        WoodType.PostOak,
        WoodType.Hickory,
        WoodType.Mesquite,
        WoodType.Pecan,
        WoodType.Apple,
        WoodType.Cherry,
        WoodType.Maple,
        WoodType.Quebracho,
        WoodType.Espinillo,
        WoodType.Other,
    ];

    private static readonly Dictionary<FuelForm, string> FuelFormKeys = new()
    {
        [FuelForm.Split] = AppStrings.FuelForm_Split,
        [FuelForm.Chunk] = AppStrings.FuelForm_Chunk,
        [FuelForm.Charcoal] = AppStrings.FuelForm_Charcoal,
        [FuelForm.Pellets] = AppStrings.FuelForm_Pellets,
    };

    public static string KeyFor(FuelForm form)
        => FuelFormKeys.TryGetValue(form, out var key) ? key : AppStrings.FuelForm_Split;

    public static IReadOnlyList<FuelForm> FuelFormsInDisplayOrder { get; } =
    [
        FuelForm.Split,
        FuelForm.Chunk,
        FuelForm.Charcoal,
        FuelForm.Pellets,
    ];

    private static readonly Dictionary<SizeClass, string> SizeClassKeys = new()
    {
        [SizeClass.Small] = AppStrings.SizeClass_Small,
        [SizeClass.Medium] = AppStrings.SizeClass_Medium,
        [SizeClass.Large] = AppStrings.SizeClass_Large,
    };

    public static string KeyFor(SizeClass sizeClass)
        => SizeClassKeys.TryGetValue(sizeClass, out var key) ? key : AppStrings.SizeClass_Medium;

    /// <summary>
    /// Small, medium, large — in that order, because the three buttons are the
    /// fast path's second tap and their positions have to be muscle memory.
    /// </summary>
    public static IReadOnlyList<SizeClass> SizeClassesInDisplayOrder { get; } =
    [
        SizeClass.Small,
        SizeClass.Medium,
        SizeClass.Large,
    ];

    private static readonly Dictionary<EventType, string> EventTypeKeys = new()
    {
        [EventType.Wrapped] = AppStrings.EventType_Wrapped,
        [EventType.Spritzed] = AppStrings.EventType_Spritzed,
        [EventType.Rested] = AppStrings.EventType_Rested,
        [EventType.Other] = AppStrings.EventType_Other,
    };

    public static string KeyFor(EventType type)
        => EventTypeKeys.TryGetValue(type, out var key) ? key : AppStrings.EventType_Other;

    public static IReadOnlyList<EventType> EventTypesInDisplayOrder { get; } =
    [
        EventType.Wrapped,
        EventType.Spritzed,
        EventType.Rested,
        EventType.Other,
    ];
}
