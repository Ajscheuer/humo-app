using Humo.Api.Entitlements;
using Humo.Api.Tests.Support;
using Humo.Shared.Entitlements;

namespace Humo.Api.Tests.Entitlements;

public class EntitlementServiceTests : IAsyncLifetime
{
    private readonly EntitlementTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task An_account_nobody_has_bought_anything_for_is_free()
    {
        var state = await _ctx.Service.GetAsync(Guid.NewGuid());

        Assert.Equal(EntitlementLevel.Free, state.Level);
        Assert.False(state.IsPro);
    }

    [Fact]
    public async Task A_purchase_makes_an_account_pro()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase());

        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task The_free_history_limit_comes_from_configuration()
    {
        _ctx.Options.FreeCookHistoryLimit = 9;

        var state = await _ctx.Service.GetAsync(_ctx.Account);

        // product-spec.md 5.1: changing the number is configuration, not a
        // release, which is only true if nothing bakes it in.
        Assert.Equal(9, state.Policy.FreeCookHistoryLimit);
    }

    [Fact]
    public async Task A_negative_configured_limit_is_not_passed_on()
    {
        _ctx.Options.FreeCookHistoryLimit = -3;

        var state = await _ctx.Service.GetAsync(_ctx.Account);

        // A typo in App Service configuration, which would otherwise have the
        // client comparing a count against nonsense.
        Assert.Equal(0, state.Policy.FreeCookHistoryLimit);
    }

    [Fact]
    public async Task An_entitlement_the_server_does_not_recognise_is_not_pro()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(entitlements: ["some_other_app_tier"]));

        Assert.False((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task Entitlement_ids_are_matched_regardless_of_case()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(entitlements: ["PRO"]));

        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_second_product_mapped_to_pro_also_counts()
    {
        _ctx.Options.ProEntitlementIds = ["pro", "founder"];

        await _ctx.Service.ApplyAsync(_ctx.APurchase(entitlements: ["founder"]));

        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_subscription_that_has_run_out_is_no_longer_pro()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(expiresAt: _ctx.Now.AddDays(30)));

        _ctx.Time.Advance(TimeSpan.FromDays(31));

        // Checked on read, so nothing has to run on a schedule for a lapsed
        // subscription to stop being Pro.
        Assert.False((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_subscription_is_pro_right_up_to_its_expiry()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(expiresAt: _ctx.Now.AddDays(30)));

        _ctx.Time.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));

        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_lifetime_purchase_never_expires()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase(expiresAt: null));

        _ctx.Time.Advance(TimeSpan.FromDays(4000));

        // A null expiry is a purchase without an end, not one that ended
        // immediately.
        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_cancellation_keeps_access_until_the_period_ends()
    {
        var endsAt = _ctx.Now.AddDays(20);

        await _ctx.Service.ApplyAsync(_ctx.APurchase(expiresAt: endsAt));

        // RevenueCat's cancellation means auto-renew off, and still carries the
        // entitlement: the subscriber paid for this period and keeps it.
        await _ctx.Service.ApplyAsync(_ctx.APurchase(
            eventId: "evt-cancel",
            occurredAt: _ctx.Now.AddDays(1),
            expiresAt: endsAt));

        _ctx.Time.Advance(TimeSpan.FromDays(10));
        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);

        _ctx.Time.Advance(TimeSpan.FromDays(11));
        Assert.False((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task An_event_with_no_entitlements_is_a_downgrade()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase());

        await _ctx.Service.ApplyAsync(_ctx.APurchase(
            eventId: "evt-refund",
            occurredAt: _ctx.Now.AddDays(1),
            entitlements: []));

        // A refund. Treating an empty list as "no news" would leave a refunded
        // account paying nothing and keeping everything.
        Assert.False((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task A_redelivered_event_is_recognised_as_a_replay()
    {
        var purchase = _ctx.APurchase();

        Assert.Equal(EntitlementUpdate.Applied, await _ctx.Service.ApplyAsync(purchase));
        Assert.Equal(EntitlementUpdate.Replay, await _ctx.Service.ApplyAsync(purchase));
    }

    [Fact]
    public async Task A_replay_does_not_change_the_level()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase());
        await _ctx.Service.ApplyAsync(_ctx.APurchase());

        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task An_event_that_arrives_after_a_newer_one_is_ignored()
    {
        // The renewal reaches us first; the cancellation that preceded it
        // arrives afterwards. Store delivery order is not guaranteed.
        await _ctx.Service.ApplyAsync(_ctx.APurchase(
            eventId: "evt-renewal",
            occurredAt: _ctx.Now.AddDays(2),
            expiresAt: _ctx.Now.AddDays(60)));

        var outcome = await _ctx.Service.ApplyAsync(_ctx.APurchase(
            eventId: "evt-expiry",
            occurredAt: _ctx.Now.AddDays(1),
            entitlements: []));

        Assert.Equal(EntitlementUpdate.Stale, outcome);
        Assert.True((await _ctx.Service.GetAsync(_ctx.Account)).IsPro);
    }

    [Fact]
    public async Task One_accounts_purchase_does_not_make_another_account_pro()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase());

        Assert.False((await _ctx.Service.GetAsync(_ctx.OtherAccount)).IsPro);
    }

    [Fact]
    public async Task The_store_subscriber_and_product_are_kept_for_support()
    {
        await _ctx.Service.ApplyAsync(_ctx.APurchase() with { ProductId = "humo_pro_annual" });

        var row = await _ctx.RowAsync();

        Assert.Equal("humo_pro_annual", row.ProductId);
        Assert.Equal(_ctx.Now, row.UpdatedAt);
    }
}
