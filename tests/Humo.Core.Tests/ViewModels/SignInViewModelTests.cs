using Humo.Core.Identity;
using Humo.Core.Localization;
using Humo.Core.Navigation;
using Humo.Core.Tests.Support;
using Humo.Core.ViewModels;
using NSubstitute;

namespace Humo.Core.Tests.ViewModels;

public class SignInViewModelTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly InMemoryPreferences _preferences = new();
    private readonly Localizer _localizer = new();

    private AccountContext _context = null!;
    private IAccountService _accounts = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private SignInViewModel CreateViewModel(bool configured = true)
    {
        _auth.IsConfigured.Returns(configured);
        _context = new AccountContext();
        _accounts = new AccountService(_preferences, _context, _db.Ownership);
        return new SignInViewModel(_auth, _accounts, _localizer, _navigation);
    }

    private static AuthenticatedUser AUser() => new()
    {
        Subject = "entra|abc123",
        Method = SignInMethod.Email,
        Email = "cook@example.com",
    };

    [Fact]
    public async Task Continuing_as_a_guest_never_touches_the_provider()
    {
        var vm = CreateViewModel();

        await vm.ContinueAsGuestCommand.ExecuteAsync(null);

        // This is the path that has to work at a smoker with no signal.
        await _auth.DidNotReceiveWithAnyArgs().SignInAsync(default, default);
        await _navigation.Received(1).GoToAsync(AppRoutes.ActiveCook, Arg.Any<CancellationToken>());

        // And the question is not asked again next launch.
        Assert.False(_accounts.NeedsSignInChoice);
    }

    [Fact]
    public async Task A_failed_sign_in_leaves_the_question_unanswered()
    {
        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns(SignInResult.Failure(SignInOutcome.NetworkUnavailable));

        var vm = CreateViewModel();
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Email);

        // The user has not chosen yet -- they tried to sign in and could not.
        // Marking it answered would drop them past this screen next launch.
        Assert.True(_accounts.NeedsSignInChoice);
    }

    [Fact]
    public async Task Continuing_as_a_guest_works_even_with_no_tenant_configured()
    {
        var vm = CreateViewModel(configured: false);

        Assert.False(vm.CanSignIn);
        Assert.True(vm.IsUnconfigured);

        await vm.ContinueAsGuestCommand.ExecuteAsync(null);

        // A build with no tenant is a normal state, not a broken one.
        await _navigation.Received(1).GoToAsync(AppRoutes.ActiveCook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_sign_in_buttons_are_disabled_with_no_tenant_configured()
    {
        var vm = CreateViewModel(configured: false);

        // Offering a button that cannot work is worse than not offering it.
        Assert.False(vm.SignInCommand.CanExecute(SignInMethod.Apple));
    }

    [Fact]
    public async Task A_successful_sign_in_switches_account_and_moves_on()
    {
        _auth.SignInAsync(SignInMethod.Google, Arg.Any<CancellationToken>())
            .Returns(SignInResult.Success(AUser()));

        var vm = CreateViewModel();
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Google);

        Assert.NotEqual(Guid.Empty, _context.CurrentAccountId);
        Assert.False(_context.IsAnonymous);
        Assert.Null(vm.ErrorMessage);
        await _navigation.Received(1).GoToAsync(AppRoutes.ActiveCook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_says_nothing_and_stays_put()
    {
        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns(SignInResult.Failure(SignInOutcome.Cancelled));

        var vm = CreateViewModel();
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Apple);

        // The user chose to back out. Telling them their own choice failed reads
        // as an error they caused.
        Assert.Null(vm.ErrorMessage);
        await _navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default);
    }

    [Theory]
    [InlineData(SignInOutcome.NetworkUnavailable, AppStrings.SignIn_Offline)]
    [InlineData(SignInOutcome.NotConfigured, AppStrings.SignIn_Unconfigured)]
    [InlineData(SignInOutcome.Failed, AppStrings.SignIn_Failed)]
    public async Task A_failed_sign_in_explains_itself_and_stays_put(
        SignInOutcome outcome,
        string expectedKey)
    {
        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns(SignInResult.Failure(outcome));

        var vm = CreateViewModel();
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Email);

        Assert.Equal(_localizer[expectedKey], vm.ErrorMessage);
        await _navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default);
    }

    [Fact]
    public async Task The_screen_is_not_left_spinning_when_sign_in_throws()
    {
        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns<Task<SignInResult>>(_ => throw new InvalidOperationException("boom"));

        var vm = CreateViewModel();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.SignInCommand.ExecuteAsync(SignInMethod.Apple));

        // Otherwise every button stays disabled behind a spinner with no way back.
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_retry_clears_the_previous_error()
    {
        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns(SignInResult.Failure(SignInOutcome.NetworkUnavailable));

        var vm = CreateViewModel();
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Email);
        Assert.NotNull(vm.ErrorMessage);

        _auth.SignInAsync(Arg.Any<SignInMethod>(), Arg.Any<CancellationToken>())
            .Returns(SignInResult.Success(AUser()));
        await vm.SignInCommand.ExecuteAsync(SignInMethod.Email);

        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void The_screen_reads_in_Spanish()
    {
        var vm = CreateViewModel();

        _localizer.SetCulture(new System.Globalization.CultureInfo("es"));

        Assert.Equal("Bienvenido a Humo", vm.Title);
    }
}
