using Humo.Api.Data;
using Humo.Shared.Analytics;
using Humo.Shared.Enums;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Analytics;

/// <summary>Computing and serving a cook's analytics.</summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Recomputes one cook and refreshes the baseline for its (meat type,
    /// equipment) pair. Called when a cook arrives finished at sync.
    /// </summary>
    Task RecomputeAsync(Guid accountId, Guid cookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cached numbers for one cook, or null when the cook is not this
    /// account's or has never been computed.
    /// </summary>
    Task<CookAnalytics?> GetAsync(Guid accountId, Guid cookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes every finished cook a pushed batch could have changed.
    /// <para>
    /// Not just the cooks in the batch. A push is capped, so a long cook's
    /// readings can arrive after the cook row itself, and readings and fuel
    /// belong to the rig rather than the cook — so a later batch of either has
    /// to find the finished cooks whose window it falls in. Without that, a
    /// cook's numbers are computed once from whatever had arrived by then and
    /// never revisited.
    /// </para>
    /// </summary>
    Task RecomputeForPushAsync(
        Guid accountId,
        SyncPushRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class AnalyticsService : IAnalyticsService
{
    /// <summary>Metrics that carry a baseline, and how to read one off a row.</summary>
    private static readonly (AnalyticsMetric Metric, Func<CookAnalyticsRow, double?> Value)[] Metrics =
    [
        (AnalyticsMetric.TimePerKg, r => r.TimePerKg),
        (AnalyticsMetric.StallDuration, r => r.StallHours),
        (AnalyticsMetric.PitStability, r => r.PitStabilityScore),
        (AnalyticsMetric.FuelEfficiency, r => r.FuelEfficiency),
    ];

    private readonly HumoDbContext _db;
    private readonly TimeProvider _time;

    public AnalyticsService(HumoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task RecomputeForPushAsync(
        Guid accountId,
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var affected = new HashSet<Guid>();

        // Cooks the batch carried, and cooks its per-cook children point at.
        foreach (var id in request.Cooks.Select(c => c.Id)
                     .Concat(request.TempEntries.Select(t => t.CookId))
                     .Concat(request.Events.Select(e => e.CookId)))
        {
            affected.Add(id);
        }

        // Rig-scoped records name no cook, so the cooks they belong to are
        // whichever finished ones were running when they were recorded.
        var rigRecords = request.PitTempEntries
            .Select(p => (p.EquipmentId, p.RecordedAt))
            .Concat(request.FuelEvents.Select(f => (f.EquipmentId, f.RecordedAt)))
            .ToList();

        foreach (var rig in rigRecords.GroupBy(r => r.EquipmentId))
        {
            var from = rig.Min(r => r.RecordedAt);
            var to = rig.Max(r => r.RecordedAt);
            var equipmentId = rig.Key;

            var overlapping = await _db.Cooks
                .Where(c => c.AccountId == accountId
                            && c.EquipmentId == equipmentId
                            && c.DeletedAt == null
                            && c.FinishedAt != null
                            && c.StartedAt <= to
                            && c.FinishedAt >= from)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var id in overlapping)
            {
                affected.Add(id);
            }
        }

        // Deduped: a batch carrying a cook and its readings must not recompute
        // it twice, and the baseline for a pair is refreshed once at the end
        // rather than after every cook in it.
        var pairs = new HashSet<(MeatType MeatType, Guid EquipmentId)>();

        foreach (var cookId in affected)
        {
            if (await ComputeAsync(accountId, cookId, cancellationToken).ConfigureAwait(false) is { } pair)
            {
                pairs.Add(pair);
            }
        }

        foreach (var (meatType, equipmentId) in pairs)
        {
            await RefreshBaselineAsync(accountId, meatType, equipmentId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task RecomputeAsync(
        Guid accountId,
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        if (await ComputeAsync(accountId, cookId, cancellationToken).ConfigureAwait(false) is { } pair)
        {
            await RefreshBaselineAsync(accountId, pair.MeatType, pair.EquipmentId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes one cook's numbers and returns the pair whose baseline it belongs
    /// to, or null when there was nothing to compute.
    /// </summary>
    private async Task<(MeatType MeatType, Guid EquipmentId)?> ComputeAsync(
        Guid accountId,
        Guid cookId,
        CancellationToken cancellationToken)
    {
        var cook = await _db.Cooks
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Id == cookId, cancellationToken)
            .ConfigureAwait(false);

        if (cook is null || cook.DeletedAt is not null || cook.FinishedAt is null)
        {
            // Nothing to compute for a cook still running: every number here is
            // about a finished cook, and a partial one would enter the baseline
            // as an unusually fast brisket.
            return null;
        }

        var row = await _db.CookAnalytics
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.CookId == cookId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new CookAnalyticsRow { AccountId = accountId, CookId = cookId };
            _db.CookAnalytics.Add(row);
        }

        await FillAsync(row, cook, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (cook.MeatType, cook.EquipmentId);
    }

    private async Task FillAsync(CookAnalyticsRow row, CookRow cook, CancellationToken cancellationToken)
    {
        // Hoisted to locals: comparing against a nullable DateTimeOffset? in a
        // LINQ predicate is something EF will not translate, and it fails at
        // query time rather than at build time.
        var startedAt = cook.StartedAt;
        var finishedAt = cook.FinishedAt!.Value;
        var accountId = cook.AccountId;
        var equipmentId = cook.EquipmentId;
        var cookId = cook.Id;

        var duration = finishedAt - startedAt;

        // A finish before the start is not a zero-length cook, it is a wrong
        // one. Slice 3 already decided this yields nothing rather than a clamped
        // zero, and the two sides must agree or the same cook reads differently
        // on the summary and in the trend.
        var validDuration = duration > TimeSpan.Zero ? duration : (TimeSpan?)null;

        var meatRows = await _db.TempEntries
            .Where(t => t.AccountId == accountId && t.CookId == cookId && t.DeletedAt == null)
            .Select(t => new { t.RecordedAt, t.MeatTempC })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var meatReadings = meatRows.Select(t => new Reading(t.RecordedAt, t.MeatTempC)).ToList();

        // Pit readings belong to the rig, not the cook, so they are read for the
        // window this cook occupied. Two cooks sharing a fire correctly share
        // one pit line, which is the whole reason PitTempEntry is rig-scoped.
        var pitRows = await _db.PitTempEntries
            .Where(p => p.AccountId == accountId
                        && p.EquipmentId == equipmentId
                        && p.DeletedAt == null
                        && p.RecordedAt >= startedAt
                        && p.RecordedAt <= finishedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fuelCount = await _db.FuelEvents
            .CountAsync(
                f => f.AccountId == accountId
                     && f.EquipmentId == equipmentId
                     && f.DeletedAt == null
                     && f.RecordedAt >= startedAt
                     && f.RecordedAt <= finishedAt,
                cancellationToken)
            .ConfigureAwait(false);

        var stall = CookMetrics.FindStall(meatReadings);
        var pitReadings = pitRows.Select(p => new Reading(p.RecordedAt, p.PitTempC)).ToList();

        var ambientReadings = pitRows.Where(p => p.AmbientTempC is not null)
            .Select(p => p.AmbientTempC!.Value)
            .ToList();

        row.MeatType = cook.MeatType;
        row.EquipmentId = cook.EquipmentId;
        row.DurationHours = validDuration?.TotalHours;
        row.TimePerKg = CookMetrics.TimePerKg(validDuration, cook.WeightKg);
        row.StallStartedAt = stall?.StartedAt;
        row.StallEndedAt = stall?.EndedAt;
        row.StallHours = stall?.Duration.TotalHours;
        row.PitStabilityScore = CookMetrics.PitStabilityScore(pitReadings);
        row.FuelEfficiency = CookMetrics.FuelEfficiency(
            fuelCount,
            validDuration ?? TimeSpan.Zero,
            pitReadings.Count > 0 ? pitReadings.Average(p => p.TempC) : null,

            // The cook's own recorded ambient is a fallback for a rig with none
            // on its readings.
            ambientReadings.Count > 0 ? ambientReadings.Average() : cook.AmbientTempC);

        row.IsEstimated = cook.FinishReason == CookFinishReason.AutoFinished;
        row.ComputedAt = _time.GetUtcNow();
    }

    /// <summary>
    /// Recomputes the stored baseline for one (meat type, equipment) pair.
    /// <para>
    /// Auto-finished cooks are left out: their end time was inferred rather than
    /// observed, so including them would drag the user's own normal towards a
    /// number nobody actually cooked.
    /// </para>
    /// </summary>
    private async Task RefreshBaselineAsync(
        Guid accountId,
        MeatType meatType,
        Guid equipmentId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CookAnalytics
            .Where(a => a.AccountId == accountId
                        && a.MeatType == meatType
                        && a.EquipmentId == equipmentId
                        && !a.IsEstimated)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.UserBaselines
            .Where(b => b.AccountId == accountId
                        && b.MeatType == meatType
                        && b.EquipmentId == equipmentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var computedAt = _time.GetUtcNow();

        foreach (var (metric, read) in Metrics)
        {
            // Only the cooks that actually produced this metric. A cook with no
            // pit data has no stability score, and counting it as a zero would
            // both drag the mean down and inflate the sample size the UI
            // reports.
            var values = rows.Select(read).Where(v => v is not null).Select(v => v!.Value);
            var statistics = Baseline.Compute(values);

            var row = existing.FirstOrDefault(b => b.Metric == metric);

            if (row is null)
            {
                row = new UserBaselineRow
                {
                    AccountId = accountId,
                    MeatType = meatType,
                    EquipmentId = equipmentId,
                    Metric = metric,
                };
                _db.UserBaselines.Add(row);
            }

            row.SampleSize = statistics.SampleSize;
            row.Mean = statistics.Mean;
            row.StandardDeviation = statistics.StandardDeviation;
            row.ComputedAt = computedAt;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CookAnalytics?> GetAsync(
        Guid accountId,
        Guid cookId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.CookAnalytics
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.CookId == cookId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var baselines = await _db.UserBaselines
            .Where(b => b.AccountId == accountId
                        && b.MeatType == row.MeatType
                        && b.EquipmentId == row.EquipmentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var anomalies = new List<AnomalyFlag>();

        foreach (var (metric, read) in Metrics)
        {
            var stored = baselines.FirstOrDefault(b => b.Metric == metric);

            if (stored is null)
            {
                continue;
            }

            var statistics = new BaselineStatistics(stored.SampleSize, stored.Mean, stored.StandardDeviation);

            if (Baseline.Compare(metric, read(row), statistics) is { } flag)
            {
                anomalies.Add(flag);
            }
        }

        // The sample size the UI reports is the duration baseline's: it is the
        // one metric every finished cook produces, so it is the honest answer to
        // "how many cooks of this kind have you done".
        var sampleSize = baselines
            .FirstOrDefault(b => b.Metric == AnalyticsMetric.TimePerKg)?.SampleSize ?? 0;

        return new CookAnalytics
        {
            CookId = row.CookId,
            Duration = Hours(row.DurationHours),
            TimePerKg = row.TimePerKg,
            StallStartedAt = row.StallStartedAt,
            StallEndedAt = row.StallEndedAt,
            StallDuration = Hours(row.StallHours),
            PitStabilityScore = row.PitStabilityScore,
            FuelEfficiency = row.FuelEfficiency,
            Anomalies = anomalies,
            Baseline = new BaselineStatus
            {
                SampleSize = sampleSize,
                RequiredSampleSize = AnalyticsPolicy.MinimumBaselineSample,
            },
            ComputedAt = row.ComputedAt,
        };
    }

    private static TimeSpan? Hours(double? hours)
        => hours is { } value ? TimeSpan.FromHours(value) : null;
}
