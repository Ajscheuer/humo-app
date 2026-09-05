namespace Humo.Core.Identity;

/// <summary>
/// Whose data this is, right now.
/// <para>
/// Every repository reads this to scope its queries and to stamp what it writes.
/// Putting it in the repository rather than in each service is deliberate: it
/// means no caller can forget, and there is exactly one place to test that a
/// signed-in user never sees a guest's cooks or the other way round.
/// </para>
/// </summary>
public interface IAccountContext
{
    /// <summary>
    /// The account owning everything read and written. <see cref="Guid.Empty"/>
    /// before startup has resolved one, which is a state no repository call
    /// should ever see — <see cref="IAccountService.InitializeAsync"/> runs first.
    /// </summary>
    Guid CurrentAccountId { get; }

    /// <summary>True when this is a device-local account with no sign-in behind it.</summary>
    bool IsAnonymous { get; }

    /// <summary>
    /// Switches the account everything is scoped to. Called at startup, on
    /// sign-in and on sign-out; not by feature code.
    /// </summary>
    void SetCurrent(Guid accountId, bool isAnonymous);
}

/// <summary>
/// The current account, held in memory for the life of the app.
/// <para>
/// Mutable and shared on purpose: sign-in changes which data the whole app is
/// looking at, and every repository must see that change at once rather than
/// holding a copy from whenever it was constructed.
/// </para>
/// </summary>
public sealed class AccountContext : IAccountContext
{
    public Guid CurrentAccountId { get; private set; }

    public bool IsAnonymous { get; private set; }

    public void SetCurrent(Guid accountId, bool isAnonymous)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "An account id of Guid.Empty would scope every query to nothing and stamp "
                + "every new record as ownerless. Sign-out mints a fresh anonymous account "
                + "rather than clearing this.",
                nameof(accountId));
        }

        CurrentAccountId = accountId;
        IsAnonymous = isAnonymous;
    }
}
