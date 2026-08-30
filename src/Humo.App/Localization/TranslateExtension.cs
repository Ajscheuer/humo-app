using Humo.App.Services;
using Humo.Core.Localization;
using Microsoft.Maui.Controls.Xaml;

namespace Humo.App.Localization;

/// <summary>
/// XAML markup extension for user-facing strings:
/// <c>&lt;Label Text="{loc:Translate Settings_Title}" /&gt;</c>.
/// <para>
/// It returns a <em>binding</em> onto the localizer's indexer rather than a
/// resolved string, so switching language in-app re-resolves every visible
/// label without rebuilding the page.
/// </para>
/// </summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]  // resolves the localizer from DI, not from the XAML service provider
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The resource key. Prefer the constants in <see cref="AppStrings"/> when binding from code.</summary>
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException(
                $"{nameof(TranslateExtension)} requires a resource key, e.g. {{loc:Translate Settings_Title}}.");
        }

        return new Binding($"[{Key}]", BindingMode.OneWay, source: ServiceHelper.GetRequiredService<ILocalizer>());
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ProvideValue(serviceProvider);
}
