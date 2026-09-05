namespace Humo.Shared.Entities;

/// <summary>
/// Fields every synced record carries.
/// <para>
/// <see cref="Id"/> is generated on the device, so a record has its final
/// identity before it has ever seen a network and no server round trip ever
/// renumbers it. Every instant is UTC.
/// </para>
/// <para>
/// <c>syncedAt</c> is deliberately absent: it is local bookkeeping, never sent,
/// so it lives on the persistence record in Humo.Core rather than on the shared
/// contract.
/// </para>
/// </summary>
public abstract class Entity
{
    /// <summary>Client-generated, never reassigned.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning account. The server derives this from the auth token and ignores
    /// whatever a client sends. Empty until accounts ship, since everything is
    /// local-only until then.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>UTC, set by the creating client.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC, set on every local mutation. Drives last-write-wins at sync.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft-delete tombstone. Nothing is ever physically removed by sync.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
