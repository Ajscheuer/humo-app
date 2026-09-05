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

/// <summary>
/// What went on the fire. <see cref="Other"/> carries free text alongside it.
/// <para>
/// <see cref="Quebracho"/> and <see cref="Espinillo"/> are here from the start
/// rather than waiting for the parrilla work: they are what an asado is actually
/// burned over, and a Spanish-speaking cook meeting a wood list with no
/// quebracho in it learns immediately that the app was not built for them.
/// </para>
/// </summary>
public enum WoodType
{
    Oak = 0,
    PostOak = 1,
    Hickory = 2,
    Mesquite = 3,
    Pecan = 4,
    Apple = 5,
    Cherry = 6,
    Maple = 7,
    Quebracho = 8,
    Espinillo = 9,
    Other = 99,
}

/// <summary>
/// The physical form of the fuel. Distinct from <see cref="WoodType"/>: oak
/// arrives as a split or a chunk, and the two burn on entirely different
/// timescales, which is the whole point for the fire model.
/// </summary>
public enum FuelForm
{
    Split = 0,
    Chunk = 1,
    Charcoal = 2,
    Pellets = 3,
}

/// <summary>
/// How much went on, as the only thing the fast path asks for.
/// <para>
/// A size class rather than a weight because it is the one judgement a cook can
/// make in a second with a glove on. Weight is available on the same sheet for
/// anyone who weighs their wood, and is never required.
/// </para>
/// </summary>
public enum SizeClass
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// Milestones during a cook. Per cook, not per rig: wrapping one brisket says
/// nothing about the ribs beside it.
/// </summary>
public enum EventType
{
    Wrapped = 0,
    Spritzed = 1,
    Rested = 2,
    Other = 99,
}
