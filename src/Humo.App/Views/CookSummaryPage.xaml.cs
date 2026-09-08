using Humo.Core.Navigation;
using Humo.Core.ViewModels;

namespace Humo.App.Views;

/// <summary>
/// One finished cook's chart and statistics. Takes the cook id from the route;
/// that plumbing is the only thing here, and it forwards straight to the
/// ViewModel's load command.
/// </summary>
[QueryProperty(nameof(CookId), AppRoutes.CookIdParameter)]
public partial class CookSummaryPage : ContentPage
{
    private readonly CookSummaryViewModel _viewModel;

    public CookSummaryPage(CookSummaryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>
    /// Set by Shell from the route. An unparseable value loads nothing, and the
    /// ViewModel reports the cook as missing rather than the page failing to open.
    /// </summary>
    public string? CookId
    {
        set => _viewModel.LoadCommand.Execute(
            Guid.TryParse(value, out var id) ? id : Guid.Empty);
    }
}
