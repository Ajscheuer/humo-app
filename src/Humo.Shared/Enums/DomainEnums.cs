namespace Humo.Shared.Enums;

/// <summary>
/// The kind of rig a cook runs on.
/// <para>
/// Stored as a value and displayed through a resource lookup — never stored as
/// a display string, which is what lets the same record read correctly in both
/// English and Spanish. Values are additive and never renumbered.
/// </para>
/// </summary>
public enum EquipmentType
{
    Offset = 0,
    Kettle = 1,
    Kamado = 2,
    Wsm = 3,
    Pellet = 4,
    Parrilla = 5,
}

/// <summary>How well the cook chamber holds heat. Feeds the fire model later.</summary>
public enum InsulationLevel
{
    None = 0,
    Light = 1,
    Heavy = 2,
}

/// <summary>
/// What is being cooked. A closed enum rather than free text, because analytics
/// group by it and the UI is bilingual — free text can be neither grouped nor
/// translated. <see cref="Other"/> is the escape hatch, and cooks logged as
/// Other are excluded from cross-cook grouping.
/// </summary>
public enum MeatType
{
    Brisket = 0,
    PorkButt = 1,
    PorkRibs = 2,
    BeefRibs = 3,
    Chicken = 4,
    Turkey = 5,
    PorkLoin = 6,
    Lamb = 7,
    Sausage = 8,
    Other = 99,
}

/// <summary>
/// Where a temperature reading came from.
/// <para>
/// Only <see cref="Manual"/> is produced today. <see cref="Probe"/> and
/// <see cref="Import"/> are reserved for fire model Level 3, so that continuous
/// probe data lands in the same tables rather than a parallel set.
/// </para>
/// </summary>
public enum TempSource
{
    Manual = 0,
    Probe = 1,
    Import = 2,
}

/// <summary>
/// How a cook ended. Null while it is still running.
/// <para>
/// <see cref="AutoFinished"/> marks a cook the app closed after 72 idle hours.
/// Its end time is inferred rather than asserted by the user, so those cooks are
/// excluded from duration and time-per-kg baselines.
/// </para>
/// </summary>
public enum CookFinishReason
{
    Manual = 0,
    AutoFinished = 1,
}
