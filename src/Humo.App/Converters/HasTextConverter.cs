using System.Globalization;

namespace Humo.App.Converters;

/// <summary>
/// True when a bound string has something in it. Used to show a message row only
/// when there is a message — binding a string straight to <c>IsVisible</c> would
/// leave an empty row taking up space, and running it through a bool converter
/// would report "visible" for every string, empty ones included.
/// </summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && !string.IsNullOrWhiteSpace(text);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Message visibility is display-only.");
}
