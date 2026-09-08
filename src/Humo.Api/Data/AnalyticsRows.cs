using System.ComponentModel.DataAnnotations.Schema;
using Humo.Shared.Analytics;
using Humo.Shared.Enums;

namespace Humo.Api.Data;

/// <summary>
/// The server's cached metrics for one cook.
/// <para>
/// A separate table from <c>Cooks</c> on purpose (<c>data-model.md</c> §3.2):
/// recomputation rewrites this row and can never touch a number the user typed.
/// It is also not a <see cref="SyncedRow"/> — it is derived, it is never sent up
/// by a client, and giving it a sequence would push server-computed values into
/// the sync stream as if a device had authored them.
/// </para>
/// </summary>
[Table("CookAnalytics")]
public sealed class CookAnalyticsRow
{
    public Guid AccountId { get; set; }

    public Guid CookId { get; set; }

    /// <summary>
    /// The pair this cook's baseline is drawn from, denormalized so a baseline
    /// query does not have to join back to Cooks for every row.
    /// </summary>
    public MeatType MeatType { get; set; }

    public Guid EquipmentId { get; set; }

    public double? DurationHours { get; set; }

    public double? TimePerKg { get; set; }

    public DateTimeOffset? StallStartedAt { get; set; }

    public DateTimeOffset? StallEndedAt { get; set; }

    public double? StallHours { get; set; }

    public double? PitStabilityScore { get; set; }

    public double? FuelEfficiency { get; set; }

    /// <summary>
    /// Excluded from baselines when true. An auto-finished cook's end time was
    /// inferred rather than observed (<c>data-model.md</c> §3.2.1), so its
    /// duration would drag the user's own normal towards a number nobody cooked.
    /// </summary>
    public bool IsEstimated { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>
/// What one user usually does, for one metric on one (meat type, equipment)
/// pair.
/// <para>
/// Stored rather than aggregated per request, as <c>data-model.md</c> §2
/// specifies: anomaly flags read from here, and the sample size is what lets the
/// UI say "not enough cooks yet" honestly rather than showing an empty panel.
/// </para>
/// <para>
/// <c>architecture.md</c> §3 describes refreshing these nightly. They are
/// refreshed here when a cook in the pair finishes instead: it is the same
/// bounded work — one pair, not the whole account — and it means a user who
/// finishes their eighth brisket sees their baseline appear then rather than the
/// following morning. Scheduling remains open (architecture.md open question 3)
/// if the cost ever justifies it.
/// </para>
/// </summary>
[Table("UserBaselines")]
public sealed class UserBaselineRow
{
    public Guid AccountId { get; set; }

    public MeatType MeatType { get; set; }

    public Guid EquipmentId { get; set; }

    public AnalyticsMetric Metric { get; set; }

    /// <summary>How many cooks stand behind this. Below the minimum, no flags.</summary>
    public int SampleSize { get; set; }

    public double Mean { get; set; }

    public double StandardDeviation { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
