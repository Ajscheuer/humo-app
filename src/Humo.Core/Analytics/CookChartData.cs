namespace Humo.Core.Analytics;

/// <summary>One plotted reading. The value is in the user's unit, not storage units.</summary>
public sealed record ChartPoint(DateTimeOffset At, double Value);

/// <summary>Which line a series is, so the view can style it without matching on text.</summary>
public enum ChartSeriesKind
{
    /// <summary>The meat's internal temperature. Per cook.</summary>
    Meat = 0,

    /// <summary>The pit temperature. Belongs to the rig, so it may span cooks.</summary>
    Pit = 1,
}

/// <summary>A line on the chart. <paramref name="NameKey"/> is a resource key, never text.</summary>
public sealed record ChartSeries(
    ChartSeriesKind Kind,
    string NameKey,
    IReadOnlyList<ChartPoint> Points);

/// <summary>What a vertical mark on the time axis represents.</summary>
public enum ChartMarkerKind
{
    Fuel = 0,
    Milestone = 1,
}

/// <summary>
/// A moment worth marking on the time axis — wood going on, or a milestone.
/// <para>
/// These are what turn a temperature graph into a story: a pit dip half an hour
/// after a large split reads very differently from the same dip with nothing
/// beside it.
/// </para>
/// </summary>
public sealed record ChartMarker(DateTimeOffset At, ChartMarkerKind Kind, string LabelKey);

/// <summary>
/// Everything a cook's chart needs, as plain data.
/// <para>
/// Deliberately free of any charting library. The rendering package is a
/// <c>Humo.App</c> concern behind this abstraction, so the series, the unit
/// conversion and the marker placement — the parts that can be wrong — are all
/// unit-testable without a device or a renderer, and swapping the library later
/// is a view change rather than a rewrite.
/// </para>
/// </summary>
public sealed record CookChartData
{
    public required IReadOnlyList<ChartSeries> Series { get; init; }

    public required IReadOnlyList<ChartMarker> Markers { get; init; }

    /// <summary>
    /// The unit every value in <see cref="Series"/> is expressed in, as a
    /// resource key. Carried with the data so an axis label cannot drift from the
    /// numbers beside it.
    /// </summary>
    public required string TemperatureUnitKey { get; init; }

    /// <summary>True when there is nothing to draw — a cook with no readings.</summary>
    public bool IsEmpty => Series.All(s => s.Points.Count == 0);

    public static CookChartData Empty(string temperatureUnitKey) => new()
    {
        Series = [],
        Markers = [],
        TemperatureUnitKey = temperatureUnitKey,
    };
}
