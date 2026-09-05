using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>
/// The rig. The unit the fire model learns against, and the owner of the fire —
/// fuel events and pit temperatures belong to equipment rather than to a cook,
/// because a firebox does not know how many pieces of meat are above it.
/// </summary>
public sealed class Equipment : Entity
{
    /// <summary>User-supplied, e.g. "Old Country Brazos".</summary>
    public string Name { get; set; } = string.Empty;

    public EquipmentType Type { get; set; }

    /// <summary>Litres. Optional; feeds the fire model as a capacity hint.</summary>
    public double? FireboxVolumeL { get; set; }

    /// <summary>Litres. Optional.</summary>
    public double? CookChamberVolumeL { get; set; }

    public InsulationLevel Insulation { get; set; }

    public string? Notes { get; set; }
}
