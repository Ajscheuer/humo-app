using System.Net;
using System.Net.Http.Json;
using Humo.Api.Tests.Support;
using Humo.Shared.Entitlements;

namespace Humo.Api.Tests.Entitlements;

public class EntitlementEndpointTests : IClassFixture<SyncApiFactory>
{
    private const string Alice = "alice@example.com";

    private readonly SyncApiFactory _factory;

    public EntitlementEndpointTests(SyncApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task An_entitlement_cannot_be_read_without_a_token()
    {
        var response = await _factory.AnonymousClient().GetAsync("/entitlements/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_reads_their_own_entitlement()
    {
        var state = await _factory.ClientFor(Alice)
            .GetFromJsonAsync<EntitlementState>("/entitlements/me");

        Assert.NotNull(state);
        Assert.Equal(EntitlementLevel.Free, state.Level);
    }

    [Fact]
    public async Task The_entitlement_carries_the_policy_the_client_needs()
    {
        var state = await _factory.ClientFor(Alice)
            .GetFromJsonAsync<EntitlementState>("/entitlements/me");

        // The client must be able to apply the free limit without knowing the
        // number in advance, or product-spec.md 5.1's "configuration, not a
        // release" is not true.
        Assert.NotNull(state);
        Assert.True(state.Policy.FreeCookHistoryLimit > 0);
    }

    [Fact]
    public async Task The_webhook_refuses_a_request_with_no_secret()
    {
        var response = await _factory.AnonymousClient()
            .PostAsJsonAsync("/entitlements/revenuecat", AnEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_webhook_refuses_a_request_with_the_wrong_secret()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entitlements/revenuecat")
        {
            Content = JsonContent.Create(AnEvent()),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "not-the-secret");

        var response = await _factory.AnonymousClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_users_own_bearer_token_does_not_open_the_webhook()
    {
        // The most likely real attack: a subscriber pointing their own token at
        // the endpoint that grants entitlements.
        var response = await _factory.ClientFor(Alice)
            .PostAsJsonAsync("/entitlements/revenuecat", AnEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_webhook_grants_pro_and_the_account_can_see_it()
    {
        var subject = $"buyer-{Guid.NewGuid()}";
        var accountId = AccountIdFor(subject);

        var response = await PostWebhookAsync(AnEvent(accountId));
        response.EnsureSuccessStatusCode();

        var state = await _factory.ClientFor(subject)
            .GetFromJsonAsync<EntitlementState>("/entitlements/me");

        Assert.NotNull(state);
        Assert.True(state.IsPro);
    }

    [Fact]
    public async Task A_purchase_by_one_account_does_not_reach_another()
    {
        var buyer = $"buyer-{Guid.NewGuid()}";
        (await PostWebhookAsync(AnEvent(AccountIdFor(buyer)))).EnsureSuccessStatusCode();

        var somebodyElse = await _factory.ClientFor($"other-{Guid.NewGuid()}")
            .GetFromJsonAsync<EntitlementState>("/entitlements/me");

        Assert.NotNull(somebodyElse);
        Assert.False(somebodyElse.IsPro);
    }

    [Fact]
    public async Task A_body_the_server_cannot_act_on_is_a_bad_request()
    {
        var response = await PostWebhookAsync(new { @event = new { id = "e" } });

        // Not a 200: this did not work. Not a 500 either, and not a retry loop.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_redelivered_webhook_is_accepted_rather_than_retried_forever()
    {
        var body = AnEvent(AccountIdFor($"buyer-{Guid.NewGuid()}"));

        (await PostWebhookAsync(body)).EnsureSuccessStatusCode();
        var second = await PostWebhookAsync(body);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private Task<HttpResponseMessage> PostWebhookAsync(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/entitlements/revenuecat")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Authorization", SyncApiFactory.WebhookSecret);

        return _factory.AnonymousClient().SendAsync(request);
    }

    /// <summary>
    /// The account the API derives from a token subject. The webhook names an
    /// account id, so a test that grants Pro and then reads it back has to agree
    /// with the resolver on which account that is.
    /// </summary>
    private static Guid AccountIdFor(string subject)
        => Humo.Api.Auth.ClaimsAccountResolver.DeterministicAccountId(subject);

    private static object AnEvent(Guid? accountId = null) => new
    {
        @event = new
        {
            id = $"evt-{Guid.NewGuid()}",
            type = "INITIAL_PURCHASE",
            app_user_id = (accountId ?? Guid.NewGuid()).ToString(),
            event_timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            product_id = "humo_pro_monthly",
            entitlement_ids = new[] { "pro" },
        },
    };
}
