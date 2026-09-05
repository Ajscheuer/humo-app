using Humo.Shared.Enums;

namespace Humo.Shared.Entities;

/// <summary>
/// A milestone during a cook — wrapped, spritzed, rested.
/// <para>
/// Per cook rather than per rig, unlike fuel: wrapping one brisket says nothing
/// about the ribs beside it on the same fire.
/// </para>
/// </summary>
public sealed class Event : Entity
{
    public Guid CookId { get; set; }

    /// <summary>UTC. Editable for the same reason a temperature reading is.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public EventType Type { get; set; }

    public string? Note { get; set; }
}
