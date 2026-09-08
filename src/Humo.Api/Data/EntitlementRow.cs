using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humo.Shared.Entitlements;

namespace Humo.Api.Data;

/// <summary>
/// What one account has bought, as the store told us.
/// <para>
/// Written only by the store's webhook, never by the app. A client saying it is
/// Pro is a claim; this row is the record, and every server-side gate reads it
/// rather than anything in a request.
/// </para>
/// </summary>
[Table("Entitlements")]
public sealed class EntitlementRow
{
    [Key]
    public Guid AccountId { get; set; }

    public EntitlementLevel Level { get; set; }

    /// <summary>
    /// End of the paid period, or null for a lifetime purchase. Compared against
    /// the clock on every read, so an expiry that arrives while the server is
    /// idle still takes effect — nothing has to run on a schedule for a lapsed
    /// subscription to stop being Pro.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The store's own identifier for the subscriber, for support.</summary>
    public string? StoreSubscriberId { get; set; }

    /// <summary>The product bought, for support and for telling plans apart.</summary>
    public string? ProductId { get; set; }

    /// <summary>When the store last told us anything about this account.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The store event that produced this row, so a replayed or out-of-order
    /// webhook can be recognised rather than applied twice.
    /// </summary>
    public string? LastEventId { get; set; }

    /// <summary>
    /// When the store says that event happened. A webhook that arrives late,
    /// after a newer one, must not overwrite the newer state.
    /// </summary>
    public DateTimeOffset? LastEventAt { get; set; }
}
