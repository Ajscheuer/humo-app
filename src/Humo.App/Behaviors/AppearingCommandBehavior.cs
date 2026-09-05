using System.Windows.Input;

namespace Humo.App.Behaviors;

/// <summary>
/// Runs a command when the page it is attached to appears.
/// <para>
/// This exists so pages can refresh on navigation without an <c>OnAppearing</c>
/// override: code-behind is the constructor and <c>InitializeComponent()</c>
/// only (CLAUDE.md), and "reload when the user comes back to this screen" is
/// behaviour a ViewModel owns.
/// </para>
/// </summary>
public sealed class AppearingCommandBehavior : Behavior<Page>
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(AppearingCommandBehavior));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttachedTo(Page page)
    {
        base.OnAttachedTo(page);

        // The behavior is not in the page's visual tree, so it does not inherit
        // the BindingContext on its own. Without this the Command binding never
        // resolves and the page silently never reloads.
        BindingContext = page.BindingContext;
        page.BindingContextChanged += OnPageBindingContextChanged;
        page.Appearing += OnPageAppearing;
    }

    protected override void OnDetachingFrom(Page page)
    {
        page.Appearing -= OnPageAppearing;
        page.BindingContextChanged -= OnPageBindingContextChanged;
        base.OnDetachingFrom(page);
    }

    private void OnPageBindingContextChanged(object? sender, EventArgs e)
        => BindingContext = (sender as Page)?.BindingContext;

    private void OnPageAppearing(object? sender, EventArgs e)
    {
        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }
}
