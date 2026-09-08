using Humo.Core.Entitlements;
using Humo.Shared.Entitlements;

namespace Humo.Core.Tests.Support;

/// <summary>
/// An entitlement the test sets directly. The caching and account-switching
/// rules are <c>ClientEntitlementServiceTests</c>' business; the screens only
/// care what the answer is.
/// </summary>
internal sealed class FakeEntitlements : IClientEntitlementService
{
    public static FakeEntitlements Unknown() => new();

    public static FakeEntitlements Free(int limit) => new()
    {
        Current = new EntitlementState
        {
            Level = EntitlementLevel.Free,
            Policy = new EntitlementPolicy { FreeCookHistoryLimit = limit },
        },
    };

    public static FakeEntitlements Pro() => new()
    {
        Current = new EntitlementState
        {
            Level = EntitlementLevel.Pro,
            Policy = new EntitlementPolicy { FreeCookHistoryLimit = 5 },
        },
    };

    public EntitlementState? Current { get; set; }

    public bool IsPro => Current?.IsPro == true;

    public int? FreeCookHistoryLimit => IsPro ? null : Current?.Policy.FreeCookHistoryLimit;

    public int Refreshes { get; private set; }

    /// <summary>What a refresh should return, and what it should leave behind.</summary>
    public EntitlementState? RefreshesTo { get; set; }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Refreshes++;

        if (RefreshesTo is null)
        {
            return Task.FromResult(false);
        }

        Current = RefreshesTo;
        return Task.FromResult(true);
    }

    public void Clear() => Current = null;
}
