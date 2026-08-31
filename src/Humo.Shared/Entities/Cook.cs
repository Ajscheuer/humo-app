using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>One cooking session: one piece of meat, on one rig, from light to rest.</summary>
public sealed class Cook : Entity
{
    public Guid EquipmentId { get; set; }

    /// <summary>
    /// The equipment's type as it was when this cook started.
    /// <para>
    /// Deliberately denormalized. The type is reachable through
    /// <see cref="EquipmentId"/>, but equipment is mutable and deletable: editing
    /// a rig from Offset to Kettle would otherwise silently rewrite the history
    /// of every cook ever run on it. The fire model groups by pit type and
    /// analytics compare across rigs, so that history has to be stable. Written
    /// once at <see cref="StartedAt"/> and never updated.
    /// </para>
    /// </summary>
    public EquipmentType PitType { get; set; }

    public MeatType MeatType { get; set; }

    /// <summary>Free text, only meaningful when <see cref="MeatType"/> is Other.</summary>
    public string? MeatTypeOther { get; set; }

    /// <summary>
    /// Kilograms. Required, but pre-filled from the meat type so it never blocks
    /// anyone: it feeds the fire model's thermal load for the whole rig, so one
    /// unweighted cook would corrupt predictions for everything sharing that fire.
    /// </summary>
    public double WeightKg { get; set; }

    /// <summary>Optional: a parrilla cook working by feel has no target temp.</summary>
    public double? TargetInternalTempC { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the cook is in progress.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Ambient at the start. Later readings ride on PitTempEntry.</summary>
    public double? AmbientTempC { get; set; }

    public string? Notes { get; set; }

    /// <summary>1-5 on the result — how the food turned out, not how well it was run.</summary>
    public int? Rating { get; set; }

    /// <summary>How the cook ended. Null while in progress.</summary>
    public CookFinishReason? FinishReason { get; set; }

    /// <summary>
    /// Latest of <see cref="StartedAt"/> and any entry logged against this cook.
    /// Drives the stale-cook rules: idle for 24h goes stale and stops counting
    /// toward the rig's thermal load, idle for 72h is auto-finished.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>True once the cook has an end time, however it got one.</summary>
    public bool IsFinished => FinishedAt is not null;
}
