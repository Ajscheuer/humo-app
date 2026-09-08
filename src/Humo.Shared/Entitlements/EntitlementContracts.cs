namespace Humo.Shared.Entitlements;

/// <summary>What an account is entitled to.</summary>
public enum EntitlementLevel
{
    /// <summary>Everything a cook needs. History is capped at the free limit.</summary>
    Free = 0,

    /// <summary>Unlimited history, synced photos, cross-cook analytics, the fire model.</summary>
    Pro = 1,
}

/// <summary>
/// The tier numbers, served by the server rather than compiled into the client.
/// <para>
/// The free history limit in particular: <c>product-spec.md</c> §5.1 requires
/// changing it to be configuration rather than an app release, and a client
/// constant would make it neither.
/// </para>
/// </summary>
public sealed record EntitlementPolicy
{
    /// <summary>
    /// How many of the most recent cooks a free account can open. Older cooks
    /// stay listed, dated and named, with their contents behind the paywall —
    /// never hidden, because an empty list reads as data loss and a locked list
    /// reads as an offer.
    /// </summary>
    public required int FreeCookHistoryLimit { get; init; }
}

/// <summary>
/// An account's entitlement as the client is told it.
/// <para>
/// The server is authoritative. This is cached on the device so the UI can
/// decide what to show while offline, and for nothing else: anything the server
/// computes checks the entitlement server-side, where a modified client cannot
/// reach it.
/// </para>
/// </summary>
public sealed record EntitlementState
{
    public required EntitlementLevel Level { get; init; }

    /// <summary>
    /// When the current subscription period ends, or null for a free account
    /// and for a lifetime purchase.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public required EntitlementPolicy Policy { get; init; }

    public bool IsPro => Level == EntitlementLevel.Pro;
}
