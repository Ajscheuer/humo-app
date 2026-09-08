namespace Humo.Core.Entitlements;

/// <summary>One thing the user can buy, as the store describes it.</summary>
public sealed record StoreProduct
{
    public required string Id { get; init; }

    /// <summary>The store's own localized title. Not a resource key: it comes from the store.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The price as the store formatted it, in the store's currency for this
    /// user's region. Never formatted here — a price is the store's to state,
    /// and getting a currency wrong on a paywall is its own kind of bad.
    /// </summary>
    public required string DisplayPrice { get; init; }

    public string? Description { get; init; }

    /// <summary>Which plan reads as the default choice on the paywall.</summary>
    public bool IsRecommended { get; init; }
}

/// <summary>How a purchase or a restore ended.</summary>
public enum PurchaseOutcome
{
    Succeeded = 0,

    /// <summary>The user backed out of the store sheet. Not an error.</summary>
    Cancelled = 1,

    /// <summary>The store could not be reached.</summary>
    NetworkUnavailable = 2,

    /// <summary>No store configured in this build.</summary>
    NotConfigured = 3,

    /// <summary>A restore that found nothing to restore.</summary>
    NothingToRestore = 4,

    /// <summary>The store refused the payment.</summary>
    Failed = 5,
}

public sealed record PurchaseResult
{
    public required PurchaseOutcome Outcome { get; init; }

    public bool IsSuccess => Outcome == PurchaseOutcome.Succeeded;

    public static PurchaseResult Of(PurchaseOutcome outcome) => new() { Outcome = outcome };
}

/// <summary>
/// Buying and restoring, behind an interface so the store SDK is swappable and
/// so the paywall is testable without one.
/// <para>
/// The implementation lives in <c>Humo.App</c>: buying needs the platform's
/// billing client. Nothing here names a store, so the choice of SDK is a change
/// on one side of this line only.
/// </para>
/// <para>
/// Note what this does <em>not</em> do: grant anything. A successful purchase
/// tells the app to ask the server again, and the server has already heard from
/// the store's webhook. The client never decides it is Pro.
/// </para>
/// </summary>
public interface IPurchaseService
{
    /// <summary>
    /// Whether this build has a store configured. False in a checkout with no
    /// store keys, which is why the paywall must degrade rather than present a
    /// buy button that cannot work.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// What is for sale, or empty when the store cannot be reached. Never
    /// throws: a paywall that crashes offline is worse than one that says it
    /// cannot load prices.
    /// </summary>
    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Presents the store's purchase sheet. Never throws for a cancellation or
    /// an unreachable store — those are outcomes, and backing out of a payment
    /// sheet is the most common one there is.
    /// </summary>
    Task<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-associates purchases already made with this store account. Required by
    /// both app stores, and the path for a user on a new phone.
    /// </summary>
    Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the store which account is signed in, so its webhook names an
    /// account this server recognises. Called on sign-in and sign-out.
    /// </summary>
    Task IdentifyAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The store in a build that has none.
/// <para>
/// A real implementation, not a stub that throws: a checkout with no store keys
/// has to run, and the paywall has to say plainly that purchasing is
/// unavailable rather than fail on a button press.
/// </para>
/// </summary>
internal sealed class UnavailablePurchaseService : IPurchaseService
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StoreProduct>>([]);

    public Task<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        => Task.FromResult(PurchaseResult.Of(PurchaseOutcome.NotConfigured));

    public Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(PurchaseResult.Of(PurchaseOutcome.NotConfigured));

    public Task IdentifyAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
