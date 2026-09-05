using System.Globalization;

namespace Humo.App.Converters;

/// <summary>
/// Negates a bound bool, so a page can show one thing when a flag is set and
/// another when it is not without a second property on the ViewModel.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Convert(value, targetType, parameter, culture);
}
