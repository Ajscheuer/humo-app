using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>
/// A reading of the cook chamber, and of the weather around it.
/// <para>
/// Scoped to equipment rather than to a cook: there is one fire and it has one
/// temperature. Two cooks sharing a rig could otherwise record contradicting pit
/// temps for the same instant, which would corrupt the pit stability score and
/// Level 2's expected envelope.
/// </para>
/// <para>
/// The logging screen still presents meat and pit together — the split is a
/// storage decision, not an interaction one, and must not cost a tap.
/// </para>
/// </summary>
public sealed class PitTempEntry : Entity
{
    public Guid EquipmentId { get; set; }

    /// <summary>UTC, editable for the same reason as on a meat reading.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public double PitTempC { get; set; }

    /// <summary>
    /// Ambient at this moment. Lives here rather than on the meat reading because
    /// weather is environmental: two cooks on one rig share it, as they share the
    /// fire.
    /// </summary>
    public double? AmbientTempC { get; set; }

    public string? Note { get; set; }

    public TempSource Source { get; set; } = TempSource.Manual;
}
