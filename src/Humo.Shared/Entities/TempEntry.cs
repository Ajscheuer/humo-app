using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>
/// A reading taken from the meat. Per cook, because each piece of meat has its
/// own internal temperature.
/// </summary>
public sealed class TempEntry : Entity
{
    public Guid CookId { get; set; }

    /// <summary>
    /// UTC, and editable. Cooks routinely log a reading a few minutes after
    /// taking it, and a wrong timestamp distorts both stall detection and the
    /// fire model, so this is not forced to the moment of entry.
    /// </summary>
    public DateTimeOffset RecordedAt { get; set; }

    public double MeatTempC { get; set; }

    public string? Note { get; set; }

    public TempSource Source { get; set; } = TempSource.Manual;
}
