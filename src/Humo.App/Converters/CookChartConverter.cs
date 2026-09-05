using System.Globalization;
using Humo.App.Services;
using Humo.Core.Analytics;
using Humo.Core.Localization;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Humo.App.Converters;

/// <summary>
/// Turns a <see cref="CookChartData"/> into LiveCharts series.
/// <para>
/// The entire dependency on the charting library lives here and in the XAML that
/// hosts the control. Everything upstream — which points, in which unit, in which
/// order, and where the markers fall — is decided in <c>Humo.Core</c> and covered
/// by unit tests, so this converter has no decisions of its own to get wrong
/// beyond colour and shape.
/// </para>
/// </summary>
public sealed class CookChartSeriesConverter : IValueConverter
{
    // Meat and pit want to be told apart at a glance, outdoors, on a phone. Two
    // clearly different hues rather than two shades of one.
    private static readonly SKColor MeatColor = new(0xC1, 0x44, 0x2E);
    private static readonly SKColor PitColor = new(0x2E, 0x6F, 0xC1);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CookChartData chart)
        {
            return Array.Empty<ISeries>();
        }

        var localizer = ServiceHelper.GetRequiredService<ILocalizer>();

        return chart.Series
            .Select(ISeries (series) => new LineSeries<DateTimePoint>
            {
                Name = localizer[series.NameKey],
                Values = series.Points
                    .Select(p => new DateTimePoint(p.At.LocalDateTime, p.Value))
                    .ToList(),
                Stroke = new SolidColorPaint(
                    series.Kind == ChartSeriesKind.Meat ? MeatColor : PitColor) { StrokeThickness = 3 },
                Fill = null,

                // No dots. A 14-hour cook logged every 20 minutes is ~40 points,
                // and a probe feed later will be thousands; the line is the
                // signal, and markers on it become noise at that density.
                GeometrySize = 0,
                LineSmoothness = 0.3,
            })
            .ToArray();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Charts are display-only.");
}

/// <summary>
/// The time axis. Separate from the series so the axis label can carry the unit
/// the series were converted into, rather than assuming one.
/// </summary>
public sealed class CookChartAxesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CookChartData chart)
        {
            return Array.Empty<ICartesianAxis>();
        }

        var localizer = ServiceHelper.GetRequiredService<ILocalizer>();

        return new ICartesianAxis[]
        {
            new Axis
            {
                // Local time on the label: a cook reads the axis against the
                // clock on their wall, not against UTC.
                Labeler = ticks => new DateTime((long)ticks)
                    .ToString("t", localizer.CurrentCulture),
                UnitWidth = TimeSpan.FromHours(1).Ticks,
                MinStep = TimeSpan.FromHours(1).Ticks,
            },
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Charts are display-only.");
}

/// <summary>
/// The temperature axis. Its name carries the unit the series were converted
/// into, taken from the data rather than re-read from settings, so the label
/// cannot disagree with the numbers beside it.
/// </summary>
public sealed class CookChartValueAxesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CookChartData chart)
        {
            return Array.Empty<ICartesianAxis>();
        }

        var localizer = ServiceHelper.GetRequiredService<ILocalizer>();

        return new ICartesianAxis[]
        {
            new Axis { Name = localizer[chart.TemperatureUnitKey] },
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Charts are display-only.");
}

/// <summary>
/// Fuel loads and milestones as vertical rules on the time axis.
/// <para>
/// These are what turn a temperature graph into a story: a pit dip half an hour
/// after a large split reads very differently from the same dip with nothing
/// beside it. Product spec §4.8 asks for them explicitly.
/// </para>
/// </summary>
public sealed class CookChartMarkersConverter : IValueConverter
{
    private static readonly SKColor FuelColor = new(0x8A, 0x5A, 0x2B);
    private static readonly SKColor MilestoneColor = new(0x6B, 0x6B, 0x6B);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CookChartData chart)
        {
            return Array.Empty<RectangularSection>();
        }

        return chart.Markers
            .Select(marker => new RectangularSection
            {
                // Zero-width: a rule at an instant, not a band over a range.
                Xi = marker.At.LocalDateTime.Ticks,
                Xj = marker.At.LocalDateTime.Ticks,
                Stroke = new SolidColorPaint(
                    marker.Kind == ChartMarkerKind.Fuel ? FuelColor : MilestoneColor)
                {
                    StrokeThickness = marker.Kind == ChartMarkerKind.Fuel ? 2 : 1,
                },
            })
            .ToArray();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Charts are display-only.");
}
