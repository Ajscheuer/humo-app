using Humo.Core.Entitlements;
using Humo.Core.Identity;
using Humo.Core.Tests.Support;
using Humo.Shared.Entitlements;

namespace Humo.Core.Tests.Entitlements;

public class ClientEntitlementServiceTests
{
    private readonly InMemoryPreferences _preferences = new();
    private readonly StubEntitlementClient _client = new();
    private readonly AccountContext _account = new();

    public ClientEntitlementServiceTests()
    {
        _account.SetCurrent(Guid.NewGuid(), isAnonymous: false);
    }

    [Fact]
    public void Before_the_server_has_ever_answered_nothing_is_known()
    {
        var service = Build();

        Assert.Null(service.Current);
        Assert.False(service.IsPro);
    }

    [Fact]
    public void An_unknown_entitlement_locks_nothing()
    {
        // Guessing a limit would be the client constant product-spec.md 5.1
        // forbids, and guessing wrong locks a subscriber out of their own
        // history the first time they open the app on a plane.
        Assert.Null(Build().FreeCookHistoryLimit);
    }

    [Fact]
    public void An_unknown_entitlement_is_not_pro()
    {
        // The paywall showing to a subscriber is a support message. Pro opening
        // for a free user is revenue.
        Assert.False(Build().IsPro);
    }

    [Fact]
    public async Task A_refresh_records_what_the_server_said()
    {
        _client.Response = Pro();

        var service = Build();

        Assert.True(await service.RefreshAsync());
        Assert.True(service.IsPro);
    }

    [Fact]
    public async Task A_free_account_gets_the_servers_limit()
    {
        _client.Response = FreeWithLimit(7);

        var service = Build();
        await service.RefreshAsync();

        Assert.Equal(7, service.FreeCookHistoryLimit);
    }

    [Fact]
    public async Task A_pro_account_has_no_limit_at_all()
    {
        _client.Response = Pro();

        var service = Build();
        await service.RefreshAsync();

        Assert.Null(service.FreeCookHistoryLimit);
    }

    [Fact]
    public async Task The_entitlement_survives_a_relaunch()
    {
        _client.Response = Pro();
        await Build().RefreshAsync();

        // A second instance over the same preferences is the next app launch,
        // and it may well be offline.
        var relaunched = Build(new StubEntitlementClient());

        Assert.True(relaunched.IsPro);
    }

    [Fact]
    public async Task An_unreachable_server_leaves_the_cached_entitlement_alone()
    {
        _client.Response = Pro();
        var service = Build();
        await service.RefreshAsync();

        _client.Response = null;

        Assert.False(await service.RefreshAsync());
        Assert.True(service.IsPro);
    }

    [Fact]
    public async Task A_guest_does_not_ask_the_server()
    {
        _account.SetCurrent(_account.CurrentAccountId, isAnonymous: true);

        Assert.False(await Build().RefreshAsync());
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task Another_account_on_the_same_phone_does_not_inherit_pro()
    {
        _client.Response = Pro();
        var service = Build();
        await service.RefreshAsync();

        // Somebody else signs in on this phone.
        _account.SetCurrent(Guid.NewGuid(), isAnonymous: false);

        Assert.False(service.IsPro);
    }

    [Fact]
    public async Task Switching_back_finds_the_first_accounts_entitlement_again()
    {
        _client.Response = Pro();
        var subscriber = _account.CurrentAccountId;
        var service = Build();
        await service.RefreshAsync();

        _account.SetCurrent(Guid.NewGuid(), isAnonymous: false);
        _account.SetCurrent(subscriber, isAnonymous: false);

        Assert.True(service.IsPro);
    }

    [Fact]
    public async Task Signing_out_forgets_the_entitlement()
    {
        _client.Response = Pro();
        var service = Build();
        await service.RefreshAsync();

        service.Clear();

        Assert.False(service.IsPro);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task A_cleared_entitlement_does_not_come_back_on_relaunch()
    {
        _client.Response = Pro();
        var service = Build();
        await service.RefreshAsync();
        service.Clear();

        Assert.False(Build().IsPro);
    }

    [Fact]
    public void A_corrupt_cache_reads_as_unknown_rather_than_crashing()
    {
        _preferences.SetString("entitlement." + _account.CurrentAccountId, "{not json");

        var service = Build();

        Assert.Null(service.Current);
        Assert.False(service.IsPro);
    }

    private IClientEntitlementService Build(StubEntitlementClient? client = null)
        => new ClientEntitlementService(client ?? _client, _preferences, _account);

    private static EntitlementState Pro() => new()
    {
        Level = EntitlementLevel.Pro,
        Policy = new EntitlementPolicy { FreeCookHistoryLimit = 5 },
    };

    private static EntitlementState FreeWithLimit(int limit) => new()
    {
        Level = EntitlementLevel.Free,
        Policy = new EntitlementPolicy { FreeCookHistoryLimit = limit },
    };

    private sealed class StubEntitlementClient : IEntitlementClient
    {
        public EntitlementState? Response { get; set; }

        public int Calls { get; private set; }

        public Task<EntitlementState?> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Response);
        }
    }
}
