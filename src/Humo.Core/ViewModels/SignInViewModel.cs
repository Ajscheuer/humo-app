using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humo.Core.Identity;
using Humo.Core.Localization;
using Humo.Core.Navigation;

namespace Humo.Core.ViewModels;

/// <summary>
/// First launch: sign in, or carry on without an account.
/// <para>
/// "Continue without an account" is not a lesser path. A guest can log cooks
/// indefinitely and offline; what they cannot do is sync or subscribe. So this
/// screen never blocks — if the provider is unreachable, or this build has no
/// tenant configured at all, the guest button still works.
/// </para>
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IAccountService _accounts;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;

    public SignInViewModel(
        IAuthService auth,
        IAccountService accounts,
        ILocalizer localizer,
        INavigationService navigation)
    {
        _auth = auth;
        _accounts = accounts;
        _localizer = localizer;
        _navigation = navigation;
    }

    public string Title => _localizer[AppStrings.SignIn_Title];

    /// <summary>
    /// Whether this build can sign anyone in. False in a checkout with no tenant,
    /// where the buttons are hidden rather than offered and then failing.
    /// </summary>
    public bool CanSignIn => _auth.IsConfigured;

    /// <summary>The counterpart, so the page can explain why the buttons are absent.</summary>
    public bool IsUnconfigured => !_auth.IsConfigured;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Why the last attempt did not sign anyone in. Null after a success.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(SignInMethod method, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _auth.SignInAsync(method, cancellationToken);

            if (!result.IsSuccess)
            {
                ErrorMessage = MessageFor(result.Outcome);
                return;
            }

            await _accounts.SignedInAsync(result.User!, cancellationToken);
            _accounts.MarkSignInChoiceMade();
            await _navigation.GoToAsync(AppRoutes.ActiveCook, cancellationToken);
        }
        finally
        {
            // In a finally: an exception escaping here would otherwise leave the
            // screen spinning with every button disabled and no way back.
            IsBusy = false;
        }
    }

    /// <summary>
    /// Carries on as a guest. Deliberately never fails and never touches the
    /// network — this is the path that has to work on a phone with no signal at
    /// the smoker.
    /// </summary>
    [RelayCommand]
    private async Task ContinueAsGuestAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        // Recorded so the question is asked once, not on every launch.
        _accounts.MarkSignInChoiceMade();
        await _navigation.GoToAsync(AppRoutes.ActiveCook, cancellationToken);
    }

    /// <summary>
    /// Cancelling is silent: the user chose to back out, and telling them their
    /// own choice failed reads as an error they caused.
    /// </summary>
    private string? MessageFor(SignInOutcome outcome) => outcome switch
    {
        SignInOutcome.Cancelled => null,
        SignInOutcome.NetworkUnavailable => _localizer[AppStrings.SignIn_Offline],
        SignInOutcome.NotConfigured => _localizer[AppStrings.SignIn_Unconfigured],
        _ => _localizer[AppStrings.SignIn_Failed],
    };
}
