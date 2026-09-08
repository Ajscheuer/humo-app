using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Humo.Api.Entitlements;

/// <summary>
/// RevenueCat's webhook body, as much of it as we act on.
/// <para>
/// Deliberately a small subset. Everything the entitlement depends on is here;
/// the rest is the store's business, and parsing fields we do not use would
/// only create ways for a payload change to break the endpoint.
/// </para>
/// </summary>
public sealed record RevenueCatWebhookBody
{
    [JsonPropertyName("event")]
    public RevenueCatEvent? Event { get; init; }
}

public sealed record RevenueCatEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The subscriber, which for Humo is the account id: the app sets its
    /// RevenueCat user id to the account it is signed into. Anything that does
    /// not parse as a GUID is not one of ours.
    /// </summary>
    [JsonPropertyName("app_user_id")]
    public string? AppUserId { get; init; }

    [JsonPropertyName("event_timestamp_ms")]
    public long? EventTimestampMs { get; init; }

    [JsonPropertyName("expiration_at_ms")]
    public long? ExpirationAtMs { get; init; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; init; }

    [JsonPropertyName("entitlement_ids")]
    public IReadOnlyList<string>? EntitlementIds { get; init; }
}

/// <summary>Turning a webhook body into something the entitlement service can apply.</summary>
public static class RevenueCatMapping
{
    /// <summary>
    /// The event as a store-neutral record, or null when the body is not one we
    /// can act on.
    /// <para>
    /// Note what this does <em>not</em> do: branch on
    /// <see cref="RevenueCatEvent.Type"/>. The entitlement ids plus the expiry
    /// say what the subscriber has, and the expiry is re-checked on every read,
    /// so a cancellation (which keeps access until the period ends) and an
    /// expiry (which does not) both come out right without this code having to
    /// enumerate a store's event vocabulary — a list that grows without asking.
    /// </para>
    /// </summary>
    public static StoreEntitlementEvent? ToStoreEvent(this RevenueCatWebhookBody? body)
    {
        if (body?.Event is not { } e
            || string.IsNullOrWhiteSpace(e.Id)
            || !Guid.TryParse(e.AppUserId, out var accountId)
            || accountId == Guid.Empty
            || e.EventTimestampMs is not { } occurredMs)
        {
            return null;
        }

        return new StoreEntitlementEvent
        {
            AccountId = accountId,
            EventId = e.Id,
            OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(occurredMs),
            ActiveEntitlementIds = e.EntitlementIds ?? [],
            ExpiresAt = e.ExpirationAtMs is { } expiresMs
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiresMs)
                : null,
            StoreSubscriberId = e.AppUserId,
            ProductId = e.ProductId,
        };
    }

    /// <summary>
    /// Whether the request carries the configured shared secret.
    /// <para>
    /// Fixed-time comparison: this header is the only thing standing between a
    /// stranger and granting themselves Pro, and a plain string comparison
    /// leaks its length and prefix to anyone willing to time the responses.
    /// </para>
    /// </summary>
    public static bool IsAuthorized(string? presented, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(presented))
        {
            // No secret configured means no store is wired up, and an endpoint
            // that grants paid access must fail closed rather than open.
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(configured));
    }
}
