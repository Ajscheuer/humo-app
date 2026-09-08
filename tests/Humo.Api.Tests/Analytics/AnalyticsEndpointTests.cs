using System.Net;
using System.Net.Http.Json;
using Humo.Api.Analytics;
using Humo.Api.Tests.Support;
using Humo.Shared.Analytics;

namespace Humo.Api.Tests.Analytics;

public class AnalyticsEndpointTests : IClassFixture<SyncApiFactory>
{
    private readonly SyncApiFactory _factory;

    public AnalyticsEndpointTests(SyncApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Analytics_cannot_be_read_without_a_token()
    {
        var response = await _factory.AnonymousClient()
            .GetAsync($"/analytics/cooks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_free_account_is_told_it_needs_pro_rather_than_forbidden()
    {
        var response = await _factory.ClientFor($"free-{Guid.NewGuid()}")
            .GetAsync($"/analytics/cooks/{Guid.NewGuid()}");

        // The caller is who they say they are and the request is well formed.
        // What is missing is a subscription, so the client shows a paywall
        // rather than an error.
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_free_account_is_refused_before_the_cook_is_even_looked_up()
    {
        // A cook that certainly does not exist. A 402 rather than a 404 means
        // the gate ran first, so a free account cannot probe which cook ids are
        // real.
        var response = await _factory.ClientFor($"free-{Guid.NewGuid()}")
            .GetAsync($"/analytics/cooks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_pro_account_reading_a_cook_it_does_not_have_gets_a_not_found()
    {
        var subject = $"pro-{Guid.NewGuid()}";
        await MakeProAsync(subject);

        var response = await _factory.ClientFor(subject)
            .GetAsync($"/analytics/cooks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_cook_id_is_not_a_route()
    {
        var subject = $"pro-{Guid.NewGuid()}";
        await MakeProAsync(subject);

        var response = await _factory.ClientFor(subject).GetAsync("/analytics/cooks/not-a-guid");

        // The route constrains the id, so this never reaches the handler.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_pro_account_reads_its_own_cooks_numbers()
    {
        var subject = $"pro-{Guid.NewGuid()}";
        await MakeProAsync(subject);

        var cookId = await ASyncedFinishedCookAsync(subject);

        var analytics = await _factory.ClientFor(subject)
            .GetFromJsonAsync<CookAnalytics>($"/analytics/cooks/{cookId}");

        Assert.NotNull(analytics);
        Assert.Equal(cookId, analytics.CookId);
        Assert.Equal(TimeSpan.FromHours(12), analytics.Duration);
        Assert.Equal(AnalyticsPolicy.MinimumBaselineSample, analytics.Baseline.RequiredSampleSize);
    }

    [Fact]
    public async Task One_pro_account_cannot_read_anothers_cook()
    {
        var owner = $"pro-owner-{Guid.NewGuid()}";
        var stranger = $"pro-stranger-{Guid.NewGuid()}";
        await MakeProAsync(owner);
        await MakeProAsync(stranger);

        var cookId = await ASyncedFinishedCookAsync(owner);

        var response = await _factory.ClientFor(stranger).GetAsync($"/analytics/cooks/{cookId}");

        // Paying for Pro buys your own analytics, not everybody's.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_account_whose_subscription_lapsed_loses_access()
    {
        var subject = $"lapsing-{Guid.NewGuid()}";
        var cookId = await ASyncedFinishedCookAsync(subject);

        // Bought, and already expired by the time it is asked about.
        await PostWebhookAsync(subject, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var response = await _factory.ClientFor(subject).GetAsync($"/analytics/cooks/{cookId}");

        // The client may still be showing a cached Pro badge. The server is
        // where that stops mattering.
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    private Task MakeProAsync(string subject)
        => PostWebhookAsync(subject, expiresAt: null);

    private async Task PostWebhookAsync(string subject, DateTimeOffset? expiresAt)
    {
        var accountId = Humo.Api.Auth.ClaimsAccountResolver.DeterministicAccountId(subject);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/entitlements/revenuecat")
        {
            Content = JsonContent.Create(new
            {
                @event = new
                {
                    id = $"evt-{Guid.NewGuid()}",
                    type = "INITIAL_PURCHASE",
                    app_user_id = accountId.ToString(),
                    event_timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    expiration_at_ms = expiresAt?.ToUnixTimeMilliseconds(),
                    product_id = "humo_pro_monthly",
                    entitlement_ids = new[] { "pro" },
                },
            }),
        };

        request.Headers.TryAddWithoutValidation("Authorization", SyncApiFactory.WebhookSecret);

        (await _factory.AnonymousClient().SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// <summary>A finished cook pushed through the real sync endpoint.</summary>
    private async Task<Guid> ASyncedFinishedCookAsync(string subject)
    {
        var startedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var rigId = Guid.NewGuid();
        var cookId = Guid.NewGuid();

        var batch = new
        {
            deviceId = Guid.NewGuid(),
            equipment = new[]
            {
                new
                {
                    id = rigId,
                    name = "Old Country Brazos",
                    type = 0,
                    insulation = 0,
                    createdAt = startedAt,
                    updatedAt = startedAt,
                },
            },
            cooks = new[]
            {
                new
                {
                    id = cookId,
                    equipmentId = rigId,
                    pitType = 0,
                    meatType = 0,
                    weightKg = 6.0,
                    startedAt,
                    finishedAt = startedAt.AddHours(12),
                    lastActivityAt = startedAt.AddHours(12),
                    createdAt = startedAt,
                    updatedAt = startedAt.AddHours(12),
                },
            },
        };

        var response = await _factory.ClientFor(subject).PostAsJsonAsync("/sync/push", batch);
        response.EnsureSuccessStatusCode();

        return cookId;
    }
}
