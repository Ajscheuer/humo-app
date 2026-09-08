using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Entitlements;
using Humo.Core.Identity;
using Humo.Core.Localization;
using Humo.Core.Navigation;

namespace Humo.Core.ViewModels;

/// <summary>The upgrade offer: what Pro adds, what it costs, and how to restore it.</summary>
public sealed partial class PaywallViewModel : ObservableObject
{
    private readonly IPurchaseService _purchases;
    private readonly IClientEntitlementService _entitlements;
    private readonly IAccountContext _account;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public PaywallViewModel(
        IPurchaseService purchases,
        IClientEntitlementService entitlements,
        IAccountContext account,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _purchases = purchases;
        _entitlements = entitlements;
        _account = account;
        _localizer = localizer;
        _navigation = navigation;
    }

    public ObservableCollection<StoreProduct> Products { get; } = [];

    public string Title => _localizer[AppStrings.Paywall_Title];

    public string Subtitle => _localizer[AppStrings.Paywall_Subtitle];

    public string RestoreLabel => _localizer[AppStrings.Paywall_Restore];

    /// <summary>What Pro adds, in the order the paywall lists it.</summary>
    public IReadOnlyList<string> Benefits =>
    [
        _localizer[AppStrings.Paywall_BenefitHistory],
        _localizer[AppStrings.Paywall_BenefitAnalytics],
        _localizer[AppStrings.Paywall_BenefitFireModel],
        _localizer[AppStrings.Paywall_BenefitPhotos],
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuy))]
    private bool _isBusy;

    /// <summary>Set when something went wrong, cleared when it is tried again.</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>True once the account is Pro — the screen becomes a thank-you.</summary>
    public bool IsAlreadyPro => _entitlements.IsPro;

    /// <summary>
    /// True for a guest. product-spec.md Decision 5a: subscribing needs an
    /// account, because a device-bound entitlement strands the user on a new
    /// phone and ends in a refund.
    /// </summary>
    public bool NeedsAccount => _account.IsAnonymous;

    /// <summary>
    /// Whether the buy buttons can do anything. False for a guest, for a build
    /// with no store, and while a purchase is in flight.
    /// </summary>
    public bool CanBuy => !IsBusy && !NeedsAccount && _purchases.IsConfigured && Products.Count > 0;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Message = null;
        Products.Clear();

        if (IsAlreadyPro)
        {
            Message = _localizer[AppStrings.Paywall_Thanks];
            Notify();
            return;
        }

        if (NeedsAccount)
        {
            Message = _localizer[AppStrings.Paywall_NeedsAccount];
            Notify();
            return;
        }

        if (!_purchases.IsConfigured)
        {
            // A checkout with no store keys. It has to say so rather than offer
            // a button that cannot work.
            Message = _localizer[AppStrings.Paywall_Unconfigured];
            Notify();
            return;
        }

        IsBusy = true;
        try
        {
            await IdentifyAsync(cancellationToken);

            foreach (var product in await _purchases.GetProductsAsync(cancellationToken))
            {
                Products.Add(product);
            }

            if (Products.Count == 0)
            {
                Message = _localizer[AppStrings.Paywall_Offline];
            }
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    /// <summary>
    /// Takes a guest to sign-in. product-spec.md §5.2 says the paywall
    /// <em>prompts</em> account creation before purchase — stating the
    /// requirement and leaving them on a dead end is not that.
    /// </summary>
    [RelayCommand]
    private Task CreateAccountAsync(CancellationToken cancellationToken)
        => _navigation.GoToAsync(AppRoutes.SignIn, cancellationToken);

    [RelayCommand]
    private async Task BuyAsync(StoreProduct product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!CanBuy)
        {
            return;
        }

        await CompleteAsync(
            () => _purchases.PurchaseAsync(product.Id, cancellationToken),
            cancellationToken);
    }

    [RelayCommand]
    private Task RestoreAsync(CancellationToken cancellationToken)
        => CompleteAsync(() => _purchases.RestoreAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Runs a store operation and then asks the server what the account is
    /// entitled to.
    /// <para>
    /// The refresh is the point. The store telling this app a purchase succeeded
    /// is not what makes the account Pro — the store's webhook told the server,
    /// and the server is the only thing whose answer counts. Skipping the
    /// refresh would leave a paying user looking at a paywall until something
    /// else happened to sync.
    /// </para>
    /// </summary>
    private async Task CompleteAsync(
        Func<Task<PurchaseResult>> operation,
        CancellationToken cancellationToken)
    {
        Message = null;
        IsBusy = true;

        try
        {
            // Before the store call, every time. The store's webhook names the
            // buyer by whatever id the store knows them as, and if that is the
            // store's own anonymous id rather than this account, the server has
            // no way to match the purchase to anybody: the money is taken and
            // nothing is granted. Idempotent, so paying the call twice is
            // cheaper than being wrong once.
            await IdentifyAsync(cancellationToken);

            var result = await operation();

            if (!result.IsSuccess)
            {
                Message = MessageFor(result.Outcome) is { } key ? _localizer[key] : null;
                return;
            }

            await _entitlements.RefreshAsync(cancellationToken);

            Message = IsAlreadyPro ? _localizer[AppStrings.Paywall_Thanks] : null;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    /// <summary>
    /// Tells the store which account this is, for anyone but a guest — a guest
    /// has no account to attach a purchase to, and naming a device-local id to
    /// the store is how an entitlement ends up stranded on the old phone.
    /// </summary>
    private Task IdentifyAsync(CancellationToken cancellationToken)
        => _account.IsAnonymous
            ? Task.CompletedTask
            : _purchases.IdentifyAsync(_account.CurrentAccountId, cancellationToken);

    /// <summary>
    /// Cancelling says nothing: the user chose to back out, and a message would
    /// be the app arguing with them.
    /// </summary>
    private static string? MessageFor(PurchaseOutcome outcome) => outcome switch
    {
        PurchaseOutcome.NetworkUnavailable => AppStrings.Paywall_Offline,
        PurchaseOutcome.NotConfigured => AppStrings.Paywall_Unconfigured,
        PurchaseOutcome.NothingToRestore => AppStrings.Paywall_NothingToRestore,
        PurchaseOutcome.Cancelled => null,
        _ => AppStrings.Paywall_Failed,
    };

    private void Notify()
    {
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(IsAlreadyPro));
        OnPropertyChanged(nameof(NeedsAccount));
    }
}
