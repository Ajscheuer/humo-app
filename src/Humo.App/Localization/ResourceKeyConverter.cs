using System.Globalization;
using Humo.App.Services;
using Humo.Core.Localization;

namespace Humo.App.Localization;

/// <summary>
/// Resolves a bound resource key into its localized string.
/// <para>
/// Use this when the key itself comes from a ViewModel (a list of options, an
/// enum display name) rather than being written in the XAML — those use
/// <c>{loc:Translate}</c> instead.
/// </para>
/// </summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? ServiceHelper.GetRequiredService<ILocalizer>()[key] : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Resource keys are display-only.");
}
