using System.Text.Json;
using Humo.Api.Entitlements;

namespace Humo.Api.Tests.Entitlements;

public class RevenueCatMappingTests
{
    private static readonly Guid Account = Guid.NewGuid();

    [Fact]
    public void A_purchase_maps_to_a_store_event()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "evt-1",
                "type": "INITIAL_PURCHASE",
                "app_user_id": "{{Account}}",
                "event_timestamp_ms": 1773468000000,
                "expiration_at_ms": 1776060000000,
                "product_id": "humo_pro_monthly",
                "entitlement_ids": ["pro"]
              }
            }
            """);

        var mapped = body.ToStoreEvent();

        Assert.NotNull(mapped);
        Assert.Equal(Account, mapped.AccountId);
        Assert.Equal("evt-1", mapped.EventId);
        Assert.Equal("humo_pro_monthly", mapped.ProductId);
        Assert.Equal(["pro"], mapped.ActiveEntitlementIds);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1776060000000), mapped.ExpiresAt);
    }

    [Fact]
    public void An_event_type_the_server_has_never_heard_of_still_maps()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "evt-new",
                "type": "SOME_FUTURE_EVENT_TYPE",
                "app_user_id": "{{Account}}",
                "event_timestamp_ms": 1773468000000,
                "entitlement_ids": ["pro"]
              }
            }
            """);

        // The entitlement ids and the expiry say what the subscriber has. Not
        // branching on the type is what keeps a store adding an event type from
        // silently becoming a hole here.
        Assert.NotNull(body.ToStoreEvent());
    }

    [Fact]
    public void An_expiry_arrives_as_no_entitlements_and_maps_to_a_downgrade()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "evt-expired",
                "type": "EXPIRATION",
                "app_user_id": "{{Account}}",
                "event_timestamp_ms": 1773468000000,
                "entitlement_ids": []
              }
            }
            """);

        Assert.Empty(body.ToStoreEvent()!.ActiveEntitlementIds);
    }

    [Fact]
    public void A_missing_entitlements_field_reads_as_none_rather_than_crashing()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "evt-2",
                "app_user_id": "{{Account}}",
                "event_timestamp_ms": 1773468000000
              }
            }
            """);

        var mapped = body.ToStoreEvent();

        Assert.NotNull(mapped);
        Assert.Empty(mapped.ActiveEntitlementIds);
    }

    [Fact]
    public void A_missing_expiry_is_a_lifetime_purchase_not_an_immediate_one()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "evt-3",
                "app_user_id": "{{Account}}",
                "event_timestamp_ms": 1773468000000,
                "entitlement_ids": ["pro"]
              }
            }
            """);

        Assert.Null(body.ToStoreEvent()!.ExpiresAt);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"event": null}""")]
    [InlineData("""{"event": {"id": "e", "app_user_id": "not-a-guid", "event_timestamp_ms": 1}}""")]
    [InlineData("""{"event": {"id": "e", "event_timestamp_ms": 1}}""")]
    [InlineData("""{"event": {"app_user_id": "8a1f0e8e-0000-4000-8000-000000000000", "event_timestamp_ms": 1}}""")]
    [InlineData("""{"event": {"id": "e", "app_user_id": "8a1f0e8e-0000-4000-8000-000000000000"}}""")]
    public void A_body_that_names_nobody_we_can_act_on_maps_to_nothing(string json)
    {
        // Rejected rather than half-applied. Every one of these would otherwise
        // become a write against Guid.Empty or a missing id.
        Assert.Null(Parse(json).ToStoreEvent());
    }

    [Fact]
    public void An_empty_account_id_is_refused()
    {
        var body = Parse($$"""
            {
              "event": {
                "id": "e",
                "app_user_id": "{{Guid.Empty}}",
                "event_timestamp_ms": 1773468000000,
                "entitlement_ids": ["pro"]
              }
            }
            """);

        // It parses as a GUID and belongs to no one. Granting Pro to it would
        // grant it to whatever else ever reads Guid.Empty.
        Assert.Null(body.ToStoreEvent());
    }

    [Fact]
    public void The_webhook_secret_must_match()
    {
        Assert.True(RevenueCatMapping.IsAuthorized("s3cret", "s3cret"));
        Assert.False(RevenueCatMapping.IsAuthorized("s3cret ", "s3cret"));
        Assert.False(RevenueCatMapping.IsAuthorized("wrong", "s3cret"));
        Assert.False(RevenueCatMapping.IsAuthorized("s3cre", "s3cret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_build_with_no_configured_secret_authorizes_nothing(string? configured)
    {
        // Fail closed. An endpoint that grants paid access must not be open
        // simply because nobody configured it yet.
        Assert.False(RevenueCatMapping.IsAuthorized("anything", configured));
        Assert.False(RevenueCatMapping.IsAuthorized(configured, configured));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_request_presenting_no_secret_is_refused(string? presented)
    {
        Assert.False(RevenueCatMapping.IsAuthorized(presented, "s3cret"));
    }

    private static RevenueCatWebhookBody Parse(string json)
        => JsonSerializer.Deserialize<RevenueCatWebhookBody>(json)!;
}
