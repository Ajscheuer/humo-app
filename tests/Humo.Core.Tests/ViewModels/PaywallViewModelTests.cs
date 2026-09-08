using Humo.Core.Entitlements;
using Humo.Core.Identity;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class PaywallViewModelTests
{
    private readonly StubPurchases _purchases = new();
    private readonly FakeEntitlements _entitlements = FakeEntitlements.Free(5);
    private readonly AccountContext _account = new();
    private readonly Localizer _localizer = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    public PaywallViewModelTests()
    {
        _account.SetCurrent(Guid.NewGuid(), isAnonymous: false);
    }

    [Fact]
    public async Task The_paywall_lists_what_is_for_sale()
    {
        _purchases.Products = [AProduct("monthly"), AProduct("annual")];

        var vm = await LoadedAsync();

        Assert.Equal(2, vm.Products.Count);
        Assert.True(vm.CanBuy);
    }

    [Fact]
    public async Task The_price_is_shown_exactly_as_the_store_gave_it()
    {
        _purchases.Products = [AProduct("annual") with { DisplayPrice = "$3.499,00" }];

        var vm = await LoadedAsync();

        // A price is the store's to state, in the user's currency and format.
        // Reformatting it here is how a paywall ends up quoting the wrong money.
        Assert.Equal("$3.499,00", vm.Products[0].DisplayPrice);
    }

    [Fact]
    public async Task A_guest_is_told_an_account_is_needed_rather_than_shown_prices()
    {
        _account.SetCurrent(_account.CurrentAccountId, isAnonymous: true);
        _purchases.Products = [AProduct("monthly")];

        var vm = await LoadedAsync();

        // Decision 5a: a device-bound entitlement strands the user on a new
        // phone and ends in a refund.
        Assert.True(vm.NeedsAccount);
        Assert.False(vm.CanBuy);
        Assert.Empty(vm.Products);
        Assert.Equal(_localizer[AppStrings.Paywall_NeedsAccount], vm.Message);
    }

    [Fact]
    public async Task A_build_with_no_store_says_so_instead_of_offering_a_button()
    {
        _purchases.IsConfigured = false;

        var vm = await LoadedAsync();

        Assert.False(vm.CanBuy);
        Assert.Equal(_localizer[AppStrings.Paywall_Unconfigured], vm.Message);
    }

    [Fact]
    public async Task An_unreachable_store_says_so_rather_than_showing_an_empty_offer()
    {
        _purchases.Products = [];

        var vm = await LoadedAsync();

        Assert.False(vm.CanBuy);
        Assert.Equal(_localizer[AppStrings.Paywall_Offline], vm.Message);
    }

    [Fact]
    public async Task A_subscriber_who_opens_the_paywall_is_thanked_not_sold_to()
    {
        _entitlements.Current = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();

        Assert.True(vm.IsAlreadyPro);
        Assert.Empty(vm.Products);
        Assert.Equal(_localizer[AppStrings.Paywall_Thanks], vm.Message);
    }

    [Fact]
    public async Task A_purchase_asks_the_server_what_the_account_is_entitled_to()
    {
        _purchases.Products = [AProduct("monthly")];
        _entitlements.RefreshesTo = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        // The store telling this app the purchase worked is not what makes the
        // account Pro. The server heard from the store's webhook, and the
        // server's answer is the only one that counts.
        Assert.Equal(1, _entitlements.Refreshes);
        Assert.True(vm.IsAlreadyPro);
    }

    [Fact]
    public async Task The_store_is_told_which_account_is_buying_before_the_purchase()
    {
        _purchases.Products = [AProduct("monthly")];
        _entitlements.RefreshesTo = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        // The store's webhook names the buyer by whatever id the store knows
        // them as. If that is the store's own anonymous id rather than this
        // account, the server cannot match the purchase to anybody, and the
        // money is taken while nothing is granted.
        Assert.Equal(_account.CurrentAccountId, _purchases.IdentifiedAs);
        Assert.True(_purchases.IdentifiedBeforePurchase);
    }

    [Fact]
    public async Task The_store_is_told_which_account_is_restoring()
    {
        _entitlements.RefreshesTo = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();
        await vm.RestoreCommand.ExecuteAsync(null);

        // A restore re-associates purchases with a store account. It has to know
        // which Humo account to attach them to.
        Assert.Equal(_account.CurrentAccountId, _purchases.IdentifiedAs);
    }

    [Fact]
    public async Task A_guest_is_never_identified_to_the_store()
    {
        _account.SetCurrent(_account.CurrentAccountId, isAnonymous: true);

        await LoadedAsync();

        // Decision 5a again: a guest has no account to attach a purchase to,
        // and telling the store about a device-local id is how entitlements end
        // up stranded on the old phone.
        Assert.Null(_purchases.IdentifiedAs);
    }

    [Fact]
    public async Task A_guest_is_taken_to_sign_in_rather_than_left_on_a_dead_end()
    {
        _account.SetCurrent(_account.CurrentAccountId, isAnonymous: true);

        var vm = await LoadedAsync();
        await vm.CreateAccountCommand.ExecuteAsync(null);

        // product-spec.md 5.2: the paywall prompts account creation before
        // purchase. Stating the requirement is not prompting.
        await _navigation.Received(1).GoToAsync(AppRoutes.SignIn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cancelled_purchase_does_not_argue_with_the_user()
    {
        _purchases.Products = [AProduct("monthly")];
        _purchases.Result = PurchaseResult.Of(PurchaseOutcome.Cancelled);

        var vm = await LoadedAsync();
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        // They chose to back out. A message would be the app answering back.
        Assert.Null(vm.Message);
        Assert.Equal(0, _entitlements.Refreshes);
    }

    [Fact]
    public async Task A_failed_purchase_says_so()
    {
        _purchases.Products = [AProduct("monthly")];
        _purchases.Result = PurchaseResult.Of(PurchaseOutcome.Failed);

        var vm = await LoadedAsync();
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        Assert.Equal(_localizer[AppStrings.Paywall_Failed], vm.Message);
        Assert.False(vm.IsAlreadyPro);
    }

    [Fact]
    public async Task A_purchase_that_could_not_reach_the_store_is_distinguishable_from_a_refusal()
    {
        _purchases.Products = [AProduct("monthly")];
        _purchases.Result = PurchaseResult.Of(PurchaseOutcome.NetworkUnavailable);

        var vm = await LoadedAsync();
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        // Different remedies: one is "try again later", the other is not.
        Assert.Equal(_localizer[AppStrings.Paywall_Offline], vm.Message);
    }

    [Fact]
    public async Task A_restore_finds_a_previous_purchase()
    {
        _entitlements.RefreshesTo = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();
        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.True(vm.IsAlreadyPro);
    }

    [Fact]
    public async Task A_restore_with_nothing_to_restore_says_so()
    {
        _purchases.Result = PurchaseResult.Of(PurchaseOutcome.NothingToRestore);

        var vm = await LoadedAsync();
        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(_localizer[AppStrings.Paywall_NothingToRestore], vm.Message);
    }

    [Fact]
    public async Task Restore_works_on_a_build_that_could_not_load_prices()
    {
        // Both app stores require a restore path, and a user on a new phone with
        // a flaky connection is exactly who needs it.
        _purchases.Products = [];
        _entitlements.RefreshesTo = FakeEntitlements.Pro().Current;

        var vm = await LoadedAsync();
        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.True(vm.IsAlreadyPro);
    }

    [Fact]
    public async Task Buying_is_refused_while_a_purchase_is_already_running()
    {
        _purchases.Products = [AProduct("monthly")];
        var vm = await LoadedAsync();

        vm.IsBusy = true;

        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        // A double tap on a payment button must not open two sheets.
        Assert.Equal(0, _purchases.PurchaseCalls);
    }

    [Fact]
    public async Task A_guest_cannot_buy_even_if_the_button_is_pressed()
    {
        _purchases.Products = [AProduct("monthly")];
        var vm = await LoadedAsync();

        _account.SetCurrent(_account.CurrentAccountId, isAnonymous: true);
        await vm.BuyCommand.ExecuteAsync(vm.Products[0]);

        Assert.Equal(0, _purchases.PurchaseCalls);
    }

    [Fact]
    public async Task The_offer_reads_in_the_current_language()
    {
        var vm = await LoadedAsync();

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        Assert.Equal(_localizer[AppStrings.Paywall_Title], vm.Title);
        Assert.All(vm.Benefits, b => Assert.False(string.IsNullOrWhiteSpace(b)));
    }

    [Fact]
    public async Task Every_benefit_is_a_real_translated_string()
    {
        var vm = await LoadedAsync();

        // A missing resource resolves to its own key, which would ship the paywall
        // showing "Paywall_BenefitHistory" to a user being asked for money.
        Assert.All(vm.Benefits, b => Assert.DoesNotContain("Paywall_", b, StringComparison.Ordinal));
    }

    private async Task<PaywallViewModel> LoadedAsync()
    {
        var vm = new PaywallViewModel(_purchases, _entitlements, _account, _localizer, _navigation);
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private static StoreProduct AProduct(string id) => new()
    {
        Id = id,
        Title = "Humo Pro",
        DisplayPrice = "$4.99",
    };

    private sealed class StubPurchases : IPurchaseService
    {
        public bool IsConfigured { get; set; } = true;

        public IReadOnlyList<StoreProduct> Products { get; set; } = [];

        public PurchaseResult Result { get; set; } = PurchaseResult.Of(PurchaseOutcome.Succeeded);

        public int PurchaseCalls { get; private set; }

        public Guid? IdentifiedAs { get; private set; }

        public bool IdentifiedBeforePurchase { get; private set; } = true;

        public Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Products);

        public Task<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        {
            if (IdentifiedAs is null)
            {
                IdentifiedBeforePurchase = false;
            }

            PurchaseCalls++;
            return Task.FromResult(Result);
        }

        public Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task IdentifyAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            IdentifiedAs = accountId;
            return Task.CompletedTask;
        }
    }
}
