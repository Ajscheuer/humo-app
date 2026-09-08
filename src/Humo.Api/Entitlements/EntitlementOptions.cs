namespace Humo.Api.Entitlements;

/// <summary>
/// The tier numbers and the webhook's shared secret, from configuration.
/// <para>
/// The history limit lives here rather than as a constant anywhere, because
/// <c>product-spec.md</c> §5.1 requires changing it to be a configuration change
/// rather than an app release. It is served to the client, which is the only
/// reason the client knows it at all.
/// </para>
/// </summary>
public sealed class EntitlementOptions
{
    public const string SectionName = "Entitlements";

    /// <summary>
    /// How many recent cooks a free account can open. The default matches
    /// <c>product-spec.md</c> Decision 1; App Service configuration overrides it
    /// without a release.
    /// </summary>
    public int FreeCookHistoryLimit { get; set; } = 5;

    /// <summary>
    /// The shared secret the store's webhook must present. Null in a build with
    /// no store configured, which makes the webhook refuse everything — the
    /// right default for an endpoint that grants paid access.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Store entitlement identifiers that mean Pro. A list because a store
    /// typically has several products — monthly, annual, lifetime — mapping to
    /// one entitlement, and because renaming one should not need a deploy.
    /// </summary>
    public IList<string> ProEntitlementIds { get; set; } = ["pro"];
}
