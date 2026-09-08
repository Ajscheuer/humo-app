using Humo.Api.Analytics;
using Humo.Api.Tests.Support;
using Humo.Shared.Analytics;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Analytics;

/// <summary>
/// Baselines and anomaly flags as a user actually accumulates them: cook after
/// cook, on one rig, syncing each time.
/// </summary>
public class BaselineIntegrationTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();
    private Equipment _rig = null!;

    public async Task InitializeAsync()
    {
        _rig = _ctx.ARig();
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [_rig] });
    }

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_first_cook_reports_how_many_more_are_needed()
    {
        var cook = await ACookAsync(hours: 12);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.False(analytics.Baseline.IsEstablished);
        Assert.Equal(1, analytics.Baseline.SampleSize);
        Assert.Equal(7, analytics.Baseline.CooksNeeded);
        Assert.Empty(analytics.Anomalies);
    }

    [Fact]
    public async Task Seven_cooks_is_still_not_enough()
    {
        Cook last = null!;
        for (var i = 0; i < 7; i++)
        {
            last = await ACookAsync(hours: 12);
        }

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, last.Id);

        Assert.NotNull(analytics);
        Assert.False(analytics.Baseline.IsEstablished);
        Assert.Equal(1, analytics.Baseline.CooksNeeded);
    }

    [Fact]
    public async Task The_eighth_cook_establishes_the_baseline()
    {
        Cook last = null!;
        for (var i = 0; i < 8; i++)
        {
            last = await ACookAsync(hours: 12 + (i * 0.1));
        }

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, last.Id);

        Assert.NotNull(analytics);
        Assert.True(analytics.Baseline.IsEstablished);
        Assert.Equal(0, analytics.Baseline.CooksNeeded);
    }

    [Fact]
    public async Task An_unusual_cook_is_flagged_once_there_is_something_to_compare_it_to()
    {
        // Seven ordinary briskets, then one that took twice as long.
        for (var i = 0; i < 7; i++)
        {
            await ACookAsync(hours: 12 + (i % 2));
        }

        var strange = await ACookAsync(hours: 26);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, strange.Id);

        Assert.NotNull(analytics);
        var flag = Assert.Single(analytics.Anomalies, a => a.Metric == AnalyticsMetric.TimePerKg);
        Assert.Equal(AnomalyDirection.High, flag.Direction);
    }

    [Fact]
    public async Task The_same_unusual_cook_is_silent_before_the_eighth()
    {
        for (var i = 0; i < 5; i++)
        {
            await ACookAsync(hours: 12 + (i % 2));
        }

        var strange = await ACookAsync(hours: 26);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, strange.Id);

        // Identical cook, identical outlier, and no flag: the baseline has not
        // earned an opinion yet.
        Assert.NotNull(analytics);
        Assert.Empty(analytics.Anomalies);
    }

    [Fact]
    public async Task A_different_meat_keeps_its_own_baseline()
    {
        for (var i = 0; i < 8; i++)
        {
            await ACookAsync(hours: 12 + (i % 2));
        }

        var pork = await ACookAsync(hours: 12, meatType: MeatType.PorkButt);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, pork.Id);

        // "Unusual for you" means for you cooking this, on this rig. Briskets
        // must not set the expectation for a pork shoulder.
        Assert.NotNull(analytics);
        Assert.Equal(1, analytics.Baseline.SampleSize);
        Assert.Empty(analytics.Anomalies);
    }

    [Fact]
    public async Task A_different_rig_keeps_its_own_baseline()
    {
        for (var i = 0; i < 8; i++)
        {
            await ACookAsync(hours: 12 + (i % 2));
        }

        var otherRig = _ctx.ARig("The kamado");
        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Equipment = [otherRig] });

        var onKamado = await ACookAsync(hours: 12, rigId: otherRig.Id);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, onKamado.Id);

        Assert.NotNull(analytics);
        Assert.Equal(1, analytics.Baseline.SampleSize);
    }

    [Fact]
    public async Task An_auto_finished_cook_is_left_out_of_the_baseline()
    {
        for (var i = 0; i < 4; i++)
        {
            await ACookAsync(hours: 12);
        }

        await ACookAsync(hours: 60, finishReason: CookFinishReason.AutoFinished);

        var baseline = await _ctx.Db.UserBaselines
            .SingleAsync(b => b.Metric == AnalyticsMetric.TimePerKg && b.AccountId == _ctx.Account);

        // Its end time was inferred rather than observed, so counting it would
        // drag the user's own normal towards a number nobody cooked.
        Assert.Equal(4, baseline.SampleSize);
        Assert.Equal(2, baseline.Mean);
    }

    [Fact]
    public async Task One_accounts_cooks_never_reach_another_accounts_baseline()
    {
        for (var i = 0; i < 8; i++)
        {
            await ACookAsync(hours: 12);
        }

        var theirs = await _ctx.Db.UserBaselines
            .CountAsync(b => b.AccountId == _ctx.OtherAccount);

        Assert.Equal(0, theirs);
    }

    [Fact]
    public async Task A_baseline_row_is_updated_rather_than_duplicated()
    {
        for (var i = 0; i < 3; i++)
        {
            await ACookAsync(hours: 12);
        }

        var rows = await _ctx.Db.UserBaselines
            .CountAsync(b => b.AccountId == _ctx.Account && b.Metric == AnalyticsMetric.TimePerKg);

        Assert.Equal(1, rows);
    }

    /// <summary>A finished cook on the rig, synced, an hour after the last one.</summary>
    private async Task<Cook> ACookAsync(
        double hours,
        MeatType meatType = MeatType.Brisket,
        Guid? rigId = null,
        CookFinishReason? finishReason = null)
    {
        var cook = _ctx.ACook(rigId ?? _rig.Id);
        cook.MeatType = meatType;
        cook.FinishedAt = cook.StartedAt.AddHours(hours);
        cook.FinishReason = finishReason;

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Cooks = [cook] });

        _ctx.Time.Advance(TimeSpan.FromDays(1));
        return cook;
    }
}
