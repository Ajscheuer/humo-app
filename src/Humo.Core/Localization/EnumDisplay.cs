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
}
