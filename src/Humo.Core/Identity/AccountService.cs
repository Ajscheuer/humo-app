using Humo.Core.Data;
using Humo.Core.Settings;

namespace Humo.Core.Identity;

/// <summary>
/// Resolving whose data this is, and keeping that answer across launches.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Establishes the current account at startup, minting a device-local
    /// anonymous one on first launch. Must run before any repository call.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates the signed-in user with an account and switches to it.
    /// <para>
    /// The guest's existing cooks are <em>not</em> claimed here. That flow needs
    /// its own decision (product-spec.md open question 5) and is deferred; until
    /// then a guest who signs in keeps their local cooks under the anonymous
    /// account and starts empty under the real one, which is recoverable.
    /// Silently merging or silently discarding would not be.
    /// </para>
    /// </summary>
    Task<Guid> SignedInAsync(AuthenticatedUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns to a device-local account. Reuses the anonymous account this
    /// device already had, so signing out does not strand the cooks logged
    /// before signing in.
    /// </summary>
    Task SignedOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True until the user has either signed in or explicitly chosen to carry on
    /// without an account. Drives the first-launch screen, and is what stops
    /// sign-in being asked for again on every launch.
    /// </summary>
    bool NeedsSignInChoice { get; }

    /// <summary>
    /// Records that the first-launch question has been answered, either way.
    /// </summary>
    void MarkSignInChoiceMade();
}

public sealed class AccountService : IAccountService
{
    /// <summary>The device-local account, kept for the life of the install.</summary>
    internal const string AnonymousAccountKey = "account.anonymous.id";

    /// <summary>The account currently in use, anonymous or not.</summary>
    internal const string CurrentAccountKey = "account.current.id";

    /// <summary>Whether the current account is the anonymous one.</summary>
    internal const string CurrentIsAnonymousKey = "account.current.anonymous";

    /// <summary>Maps a provider subject to the account it owns, so a returning user comes back to their data.</summary>
    internal const string AccountForSubjectKeyPrefix = "account.subject.";

    /// <summary>Set once the user has answered the first-launch question, either way.</summary>
    internal const string SignInChoiceMadeKey = "account.signInChoiceMade";

    private readonly IAppPreferences _preferences;
    private readonly IAccountContext _context;
    private readonly IRecordOwnership _ownership;

    public AccountService(
        IAppPreferences preferences,
        IAccountContext context,
        IRecordOwnership ownership)
    {
        _preferences = preferences;
        _context = context;
        _ownership = ownership;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var anonymousId = ReadOrCreateAnonymousId();

        var current = ReadGuid(CurrentAccountKey) ?? anonymousId;
        var isAnonymous = current == anonymousId
                          || _preferences.GetString(CurrentIsAnonymousKey) != bool.FalseString;

        _context.SetCurrent(current, isAnonymous);
        Persist(current, isAnonymous);

        // Records written before accounts existed carry Guid.Empty and would
        // otherwise become invisible the moment scoping switched on -- every cook
        // the user had logged, silently gone. Adopt them into whatever account is
        // in use on this device.
        //
        // ConfigureAwait(false) is load-bearing, not habit: startup blocks on
        // this so the account is known before any screen reads the database, and
        // resuming on the blocked UI thread would deadlock the app on launch.
        // This is a service, so the ViewModel rule in CLAUDE.md does not apply.
        await _ownership.ClaimUnownedRecordsAsync(current, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid> SignedInAsync(
        AuthenticatedUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Subject))
        {
            throw new ArgumentException(
                "A signed-in user with no subject cannot be mapped to an account, and "
                + "minting a new one each time would strand the previous sign-in's data.",
                nameof(user));
        }

        // Same person signing in again on this device comes back to their own
        // account rather than a fresh, empty one.
        var subjectKey = AccountForSubjectKeyPrefix + user.Subject;
        var accountId = ReadGuid(subjectKey) ?? Guid.NewGuid();

        _preferences.SetString(subjectKey, accountId.ToString());
        _context.SetCurrent(accountId, isAnonymous: false);
        Persist(accountId, isAnonymous: false);

        await Task.CompletedTask;
        return accountId;
    }

    public async Task SignedOutAsync(CancellationToken cancellationToken = default)
    {
        var anonymousId = ReadOrCreateAnonymousId();

        _context.SetCurrent(anonymousId, isAnonymous: true);
        Persist(anonymousId, isAnonymous: true);

        // Deliberately leaves the choice recorded: signing out returns the user
        // to their guest data, not to the first-launch screen they already
        // answered once.
        await Task.CompletedTask;
    }

    public bool NeedsSignInChoice
        => _preferences.GetString(SignInChoiceMadeKey) != bool.TrueString;

    public void MarkSignInChoiceMade()
        => _preferences.SetString(SignInChoiceMadeKey, bool.TrueString);

    private Guid ReadOrCreateAnonymousId()
    {
        if (ReadGuid(AnonymousAccountKey) is { } existing)
        {
            return existing;
        }

        // Client-generated, like every other id in Humo: the account has its
        // final identity before it has ever seen a server, which is what lets a
        // guest log cooks offline from the first launch.
        var minted = Guid.NewGuid();
        _preferences.SetString(AnonymousAccountKey, minted.ToString());
        return minted;
    }

    private void Persist(Guid accountId, bool isAnonymous)
    {
        _preferences.SetString(CurrentAccountKey, accountId.ToString());
        _preferences.SetString(
            CurrentIsAnonymousKey, isAnonymous ? bool.TrueString : bool.FalseString);
    }

    private Guid? ReadGuid(string key)
        => Guid.TryParse(_preferences.GetString(key), out var value) && value != Guid.Empty
            ? value
            : null;
}
