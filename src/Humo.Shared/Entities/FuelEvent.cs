using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>
/// Fuel going on the fire. The record the fire model learns from.
/// <para>
/// It belongs to the <em>equipment</em>, not to a cook. A firebox does not know
/// how many pieces of meat are above it, and cooks routinely run a brisket and
/// ribs in one smoker fed by one fire. Scoping fuel to the rig means concurrent
/// cooks share one fuel series and one learned cadence, instead of the model
/// seeing every load twice and predicting roughly twice as often as the fire
/// needs. It is also what keeps logging at two taps with two cooks running:
/// there is no "which cook is this for?" question, because the answer is always
/// "the fire".
/// </para>
/// </summary>
public sealed class FuelEvent : Entity
{
    /// <summary>The fire this fed.</summary>
    public Guid EquipmentId { get; set; }

    /// <summary>
    /// The cook that was on screen when this was logged. Display only — the fire
    /// model never reads it, because a fire is not divisible between cooks.
    /// </summary>
    public Guid? CookId { get; set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public WoodType WoodType { get; set; }

    /// <summary>Only meaningful when <see cref="WoodType"/> is Other.</summary>
    public string? WoodTypeOther { get; set; }

    public FuelForm Form { get; set; }

    /// <summary>The one thing the fast path requires.</summary>
    public SizeClass SizeClass { get; set; }

    /// <summary>Pieces added. Defaults to one, which is the overwhelming case.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Optional, for cooks who weigh their wood.</summary>
    public double? WeightKg { get; set; }

    /// <summary>
    /// True when this came from an "Added log" notification response rather than
    /// the user opening the app.
    /// <para>
    /// Without this flag an event created <em>because we asked</em> is
    /// indistinguishable from one created because the fire needed it, which
    /// biases the learned cadence toward whatever cadence was already predicted.
    /// Level 1 excludes prompt-driven events from cadence learning entirely.
    /// </para>
    /// </summary>
    public bool ViaNotification { get; set; }
}
