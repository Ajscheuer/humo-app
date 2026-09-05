using Humo.Core.Identity;
using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Identity;

public class AccountServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly InMemoryPreferences _preferences = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>A service over a fresh context, as the app builds it at startup.</summary>
    private (IAccountService Service, AccountContext Context) CreateService()
    {
        var context = new AccountContext();
        return (new AccountService(_preferences, context, _db.Ownership), context);
    }

    private static AuthenticatedUser AUser(string subject = "entra|abc123") => new()
    {
        Subject = subject,
        Method = SignInMethod.Email,
        Email = "cook@example.com",
    };

    [Fact]
    public async Task First_launch_mints_an_anonymous_account()
    {
        var (service, context) = CreateService();

        await service.InitializeAsync();

        // "Continue without an account" has to work before any network call, so
        // the id is client-generated like every other id in Humo.
        Assert.NotEqual(Guid.Empty, context.CurrentAccountId);
        Assert.True(context.IsAnonymous);
    }

    [Fact]
    public async Task The_anonymous_account_survives_a_restart()
    {
        var (first, firstContext) = CreateService();
        await first.InitializeAsync();
        var original = firstContext.CurrentAccountId;

        // Same device, next launch.
        var (second, secondContext) = CreateService();
        await second.InitializeAsync();

        // A new id each launch would orphan every cook logged before it.
        Assert.Equal(original, secondContext.CurrentAccountId);
    }

    [Fact]
    public async Task Signing_in_switches_to_a_real_account()
    {
        var (service, context) = CreateService();
        await service.InitializeAsync();
        var anonymous = context.CurrentAccountId;

        var accountId = await service.SignedInAsync(AUser());

        Assert.NotEqual(anonymous, accountId);
        Assert.Equal(accountId, context.CurrentAccountId);
        Assert.False(context.IsAnonymous);
    }

    [Fact]
    public async Task Signing_in_again_returns_to_the_same_account()
    {
        var (service, context) = CreateService();
        await service.InitializeAsync();

        var first = await service.SignedInAsync(AUser());
        await service.SignedOutAsync();
        var second = await service.SignedInAsync(AUser());

        // Otherwise every sign-in would strand the previous one's data.
        Assert.Equal(first, second);
        Assert.Equal(first, context.CurrentAccountId);
    }

    [Fact]
    public async Task Two_different_people_on_one_device_get_different_accounts()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var mine = await service.SignedInAsync(AUser("entra|me"));
        var theirs = await service.SignedInAsync(AUser("entra|you"));

        Assert.NotEqual(mine, theirs);
    }

    [Fact]
    public async Task The_signed_in_account_survives_a_restart()
    {
        var (first, _) = CreateService();
        await first.InitializeAsync();
        var accountId = await first.SignedInAsync(AUser());

        var (second, secondContext) = CreateService();
        await second.InitializeAsync();

        // Reopening the app must not silently drop the user back to guest.
        Assert.Equal(accountId, secondContext.CurrentAccountId);
        Assert.False(secondContext.IsAnonymous);
    }

    [Fact]
    public async Task Signing_out_returns_to_the_same_anonymous_account()
    {
        var (service, context) = CreateService();
        await service.InitializeAsync();
        var anonymous = context.CurrentAccountId;

        await service.SignedInAsync(AUser());
        await service.SignedOutAsync();

        // Not a fresh anonymous account: the cooks logged before signing in are
        // still on this device and must come back into view.
        Assert.Equal(anonymous, context.CurrentAccountId);
        Assert.True(context.IsAnonymous);
    }

    [Fact]
    public async Task A_user_with_no_subject_is_refused()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        // Nothing to map an account to, and minting one each time would strand
        // the previous sign-in's data.
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SignedInAsync(AUser(subject: "   ")));
    }

    [Fact]
    public async Task Signing_in_does_not_claim_the_guests_cooks()
    {
        var (service, context) = CreateService();
        await service.InitializeAsync();

        _db.Account.SetCurrent(context.CurrentAccountId, isAnonymous: true);
        var guestCook = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
        });

        var accountId = await service.SignedInAsync(AUser());
        _db.Account.SetCurrent(accountId, isAnonymous: false);

        // The merge flow is deferred pending its own decision. Until then the
        // guest's cooks stay put rather than being silently merged or discarded;
        // signing out brings them back.
        Assert.Null(await _db.Cooks.GetAsync(guestCook.Id));

        await service.SignedOutAsync();
        _db.Account.SetCurrent(context.CurrentAccountId, isAnonymous: true);
        Assert.NotNull(await _db.Cooks.GetAsync(guestCook.Id));
    }

    [Fact]
    public async Task The_sign_in_question_is_asked_on_first_launch_only()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        Assert.True(service.NeedsSignInChoice);

        service.MarkSignInChoiceMade();
        Assert.False(service.NeedsSignInChoice);

        // And it stays answered across launches -- being asked to sign in every
        // time you open the app is the behaviour this flag exists to prevent.
        var (next, _) = CreateService();
        await next.InitializeAsync();
        Assert.False(next.NeedsSignInChoice);
    }

    [Fact]
    public async Task Signing_out_does_not_re_ask_the_first_launch_question()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();
        service.MarkSignInChoiceMade();

        await service.SignedInAsync(AUser());
        await service.SignedOutAsync();

        // Sign-out returns the user to their guest data, not to a screen they
        // already answered.
        Assert.False(service.NeedsSignInChoice);
    }

    [Fact]
    public async Task An_account_id_of_empty_is_refused()
    {
        var context = new AccountContext();

        // It would scope every query to nothing and stamp every new record as
        // ownerless -- a silent, total data loss that looks like an empty app.
        Assert.Throws<ArgumentException>(
            () => context.SetCurrent(Guid.Empty, isAnonymous: true));

        await Task.CompletedTask;
    }
}
