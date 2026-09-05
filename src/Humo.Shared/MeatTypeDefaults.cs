using Humo.Shared.Enums;

namespace Humo.Shared;

/// <summary>
/// Typical starting weights per meat type, in kilograms.
/// <para>
/// Weight is required on a cook because it feeds the fire model's thermal load
/// for the whole rig — one unweighted cook would corrupt predictions for
/// everything sharing that fire. These defaults are what keep "required" from
/// meaning "blocking": the field arrives pre-filled and the user adjusts it,
/// rather than being stopped at a blank box because they did not put the brisket
/// on a scale.
/// </para>
/// <para>
/// They are rough central values for a whole, untrimmed cut as bought, not
/// precise figures. Being roughly right beats being absent.
/// </para>
/// </summary>
public static class MeatTypeDefaults
{
    /// <summary>Used for <see cref="MeatType.Other"/> and any value without an entry.</summary>
    public const double FallbackWeightKg = 2.0;

    private static readonly Dictionary<MeatType, double> WeightsKg = new()
    {
        [MeatType.Brisket] = 6.0,      // ~13 lb packer
        [MeatType.PorkButt] = 4.0,     // ~9 lb bone-in shoulder
        [MeatType.PorkRibs] = 1.5,     // one rack, spares
        [MeatType.BeefRibs] = 2.5,     // one plate of three bones
        [MeatType.Chicken] = 1.8,      // one whole bird
        [MeatType.Turkey] = 6.0,       // ~13 lb whole bird
        [MeatType.PorkLoin] = 2.5,
        [MeatType.Lamb] = 2.5,         // bone-in shoulder
        [MeatType.Sausage] = 1.0,
    };

    /// <summary>
    /// A sensible starting weight for the given meat type. Never throws and never
    /// returns zero: an unknown or newly added enum value falls back rather than
    /// leaving the field blank, since a blank weight is the failure this exists
    /// to prevent.
    /// </summary>
    public static double ForMeatType(MeatType meatType)
        => WeightsKg.TryGetValue(meatType, out var weight) ? weight : FallbackWeightKg;
}
