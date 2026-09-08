using Humo.Core.Analytics;
using Humo.Core.Data;
using Humo.Core.Localization;
using Humo.Core.Settings;
using Humo.Shared.Entities;
using Humo.Shared.Units;

namespace Humo.Core.Services;

/// <summary>A finished cook and what it added up to.</summary>
public sealed record CookSummary
{
    public required Cook Cook { get; init; }

    public required CookStatistics Statistics { get; init; }

    public required CookChartData Chart { get; init; }

    /// <summary>The rig it ran on, or null if that rig has since been deleted.</summary>
    public Equipment? Equipment { get; init; }
}

/// <summary>
/// Reading a cook back after it is over: the history list, and one cook's chart
/// and statistics.
/// </summary>
public interface ICookSummaryService
{
    /// <summary>Finished cooks, newest first.</summary>
    Task<IReadOnlyList<Cook>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One cook's summary, or null if it no longer exists. Temperatures in the
    /// returned chart are already in the user's unit.
    /// </summary>
    Task<CookSummary?> GetSummaryAsync(Guid cookId, CancellationToken cancellationToken = default);
}

public sealed class CookSummaryService : ICookSummaryService
{
    private readonly ICookRepository _cooks;
    private readonly ITempEntryRepository _tempEntries;
    private readonly IPitTempEntryRepository _pitTempEntries;
    private readonly IFuelEventRepository _fuelEvents;
    private readonly IEventRepository _events;
    private readonly IEquipmentRepository _equipment;
    private readonly IUserSettings _settings;

    public CookSummaryService(
        ICookRepository cooks,
        ITempEntryRepository tempEntries,
        IPitTempEntryRepository pitTempEntries,
        IFuelEventRepository fuelEvents,
        IEventRepository events,
        IEquipmentRepository equipment,
        IUserSettings settings)
    {
        _cooks = cooks;
        _tempEntries = tempEntries;
        _pitTempEntries = pitTempEntries;
        _fuelEvents = fuelEvents;
        _events = events;
        _equipment = equipment;
        _settings = settings;
    }

    public Task<IReadOnlyList<Cook>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => _cooks.GetFinishedAsync(cancellationToken);

    public async Task<CookSummary?> GetSummaryAsync(
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        var cook = await _cooks.GetAsync(cookId, cancellationToken).ConfigureAwait(false);
        if (cook is null)
        {
            return null;
        }

        var readings = await _tempEntries.GetForCookAsync(cookId, cancellationToken)
            .ConfigureAwait(false);
        var milestones = await _events.GetForCookAsync(cookId, cancellationToken)
            .ConfigureAwait(false);

        // The pit and the fuel belong to the rig, not the cook, so both are read
        // for the window this cook occupied. A cook sharing a fire with another
        // sees the same pit line, which is correct: there was one fire.
        var (from, to) = WindowOf(cook, readings, milestones);

        var pitReadings = await _pitTempEntries
            .GetForEquipmentAsync(cook.EquipmentId, from, to, cancellationToken)
            .ConfigureAwait(false);

        var allFuel = await _fuelEvents.GetForEquipmentAsync(cook.EquipmentId, cancellationToken)
            .ConfigureAwait(false);
        var fuel = allFuel.Where(f => f.RecordedAt >= from && f.RecordedAt <= to).ToList();

        var equipment = await _equipment.GetAsync(cook.EquipmentId, cancellationToken)
            .ConfigureAwait(false);

        var unit = _settings.TemperatureUnit;

        return new CookSummary
        {
            Cook = cook,
            Equipment = equipment,
            Statistics = CookStatistics.For(cook) with
            {
                PeakMeatTempC = readings.Count > 0 ? readings.Max(r => r.MeatTempC) : null,
                PeakPitTempC = pitReadings.Count > 0 ? pitReadings.Max(p => p.PitTempC) : null,
                ReadingCount = readings.Count,
                FuelEventCount = fuel.Count,
            },
            Chart = BuildChart(readings, pitReadings, fuel, milestones, unit),
        };
    }

    /// <summary>
    /// The time span the chart covers.
    /// <para>
    /// Ends at the finish time when there is one, and otherwise at the last thing
    /// recorded — a running cook's window must not stop at its start. Readings
    /// logged slightly outside those bounds (a back-dated entry from just before
    /// the cook was created) still belong to it, so the window widens to hold
    /// them rather than silently dropping them off the chart.
    /// </para>
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) WindowOf(
        Cook cook,
        IReadOnlyList<TempEntry> readings,
        IReadOnlyList<Event> milestones)
    {
        var from = cook.StartedAt;
        var to = cook.FinishedAt ?? cook.LastActivityAt;

        foreach (var at in readings.Select(r => r.RecordedAt)
                     .Concat(milestones.Select(m => m.RecordedAt)))
        {
            if (at < from)
            {
                from = at;
            }

            if (at > to)
            {
                to = at;
            }
        }

        return (from, to);
    }

    /// <summary>
    /// Builds the chart. This is the display boundary: everything above it is
    /// Celsius, everything in the result is the user's unit.
    /// </summary>
    private static CookChartData BuildChart(
        IReadOnlyList<TempEntry> readings,
        IReadOnlyList<PitTempEntry> pitReadings,
        IReadOnlyList<FuelEvent> fuel,
        IReadOnlyList<Event> milestones,
        TemperatureUnit unit)
    {
        var unitKey = unit == TemperatureUnit.Fahrenheit
            ? AppStrings.Unit_Fahrenheit_Short
            : AppStrings.Unit_Celsius_Short;

        List<ChartSeries> series = [];

        if (readings.Count > 0)
        {
            series.Add(new ChartSeries(
                ChartSeriesKind.Meat,
                AppStrings.Chart_MeatSeries,
                readings
                    .OrderBy(r => r.RecordedAt)
                    .Select(r => new ChartPoint(
                        r.RecordedAt, UnitConversion.FromCelsius(r.MeatTempC, unit)))
                    .ToList()));
        }

        if (pitReadings.Count > 0)
        {
            series.Add(new ChartSeries(
                ChartSeriesKind.Pit,
                AppStrings.Chart_PitSeries,
                pitReadings
                    .OrderBy(p => p.RecordedAt)
                    .Select(p => new ChartPoint(
                        p.RecordedAt, UnitConversion.FromCelsius(p.PitTempC, unit)))
                    .ToList()));
        }

        var markers = fuel
            .Select(f => new ChartMarker(f.RecordedAt, ChartMarkerKind.Fuel, AppStrings.Fuel_Add))
            .Concat(milestones.Select(m => new ChartMarker(
                m.RecordedAt, ChartMarkerKind.Milestone, EnumDisplay.KeyFor(m.Type))))
            .OrderBy(m => m.At)
            .ToList();

        return new CookChartData
        {
            Series = series,
            Markers = markers,
            TemperatureUnitKey = unitKey,
        };
    }
}
