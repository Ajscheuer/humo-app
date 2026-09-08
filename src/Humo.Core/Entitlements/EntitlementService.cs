using System.Text.Json;
using Humo.Core.Identity;
using Humo.Core.Settings;
using Humo.Shared.Entitlements;

namespace Humo.Core.Entitlements;

/// <summary>
/// What this account may see, as far as the device knows.
/// <para>
/// A cache, and only a cache. <c>product-spec.md</c> §5.2 puts the authority on
/// the server: this exists so the UI can decide what to draw while offline, and
/// anything the server computes checks the entitlement again where a modified
/// client cannot reach it.
/// </para>
/// </summary>
public interface IClientEntitlementService
{
    /// <summary>
    /// The last thing the server said, or null if it has never said anything —
    /// a fresh install that has not been online yet, or a guest.
    /// </summary>
    EntitlementState? Current { get; }

    /// <summary>
    /// True only when the server has said so. Unknown is not Pro: the paywall
    /// showing to a subscriber is a support message, and Pro features opening
    /// for a free user is revenue.
    /// </summary>
    bool IsPro { get; }

    /// <summary>
    /// How many recent cooks may be opened, or null when nobody has said.
    /// <para>
    /// Null means "do not lock anything". Guessing a number would be the client
    /// constant §5.1 forbids, and guessing wrong locks a paying user out of
    /// their own history on a plane.
    /// </para>
    /// </summary>
    int? FreeCookHistoryLimit { get; }

    /// <summary>Asks the server. Returns false when it could not be reached.</summary>
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the cached state for the current account. Called on sign-out,
    /// before the account switches, so a subscriber's tier does not sit on a
    /// shared phone after they have left.
    /// </summary>
    void Clear();
}

internal sealed class ClientEntitlementService : IClientEntitlementService
{
    private const string CacheKeyPrefix = "entitlement.";

    private readonly IEntitlementClient _client;
    private readonly IAppPreferences _preferences;
    private readonly IAccountContext _account;

    private EntitlementState? _current;
    private Guid _loadedFor;

    public ClientEntitlementService(
        IEntitlementClient client,
        IAppPreferences preferences,
        IAccountContext account)
    {
        _client = client;
        _preferences = preferences;
        _account = account;
    }

    public EntitlementState? Current
    {
        get
        {
            // Reloaded when the account changes, so signing in as somebody else
            // never shows the previous account's tier.
            if (_loadedFor != _account.CurrentAccountId)
            {
                _current = ReadCache(_account.CurrentAccountId);
                _loadedFor = _account.CurrentAccountId;
            }

            return _current;
        }
    }

    public bool IsPro => Current?.IsPro == true;

    public int? FreeCookHistoryLimit => IsPro ? null : Current?.Policy.FreeCookHistoryLimit;

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_account.IsAnonymous)
        {
            // A guest has no account on the server and therefore no entitlement
            // to fetch. Not a failure; the "continue without an account" path.
            return false;
        }

        var accountId = _account.CurrentAccountId;
        var fetched = await _client.GetAsync(cancellationToken).ConfigureAwait(false);

        if (fetched is null)
        {
            // Offline, or the token was refused. The cached state stands: a
            // subscriber on a plane keeps their history.
            return false;
        }

        WriteCache(accountId, fetched);

        _current = fetched;
        _loadedFor = accountId;

        return true;
    }

    public void Clear()
    {
        _preferences.Remove(CacheKeyPrefix + _account.CurrentAccountId);

        _current = null;
        _loadedFor = Guid.Empty;
    }

    // Per account: two people on one phone must not inherit each other's tier,
    // and a signed-out guest must not keep the subscriber's.
    private EntitlementState? ReadCache(Guid accountId)
    {
        var stored = _preferences.GetString(CacheKeyPrefix + accountId);

        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EntitlementState>(stored);
        }
        catch (JsonException)
        {
            // A cache written by an older version, or corrupted. Unknown rather
            // than a crash, and the next refresh replaces it.
            return null;
        }
    }

    private void WriteCache(Guid accountId, EntitlementState state)
        => _preferences.SetString(CacheKeyPrefix + accountId, JsonSerializer.Serialize(state));
}
