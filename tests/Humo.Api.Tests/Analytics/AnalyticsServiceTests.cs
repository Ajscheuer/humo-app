using Humo.Api.Analytics;
using Humo.Api.Tests.Support;
using Humo.Shared.Analytics;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Analytics;

/// <summary>
/// Analytics as they actually run: pushed through sync, computed from the rows
/// that arrived. The arithmetic is covered by the pure tests; this covers what
/// gets fed into it and what comes back out.
/// </summary>
public class AnalyticsServiceTests : IAsyncLifetime
{
    private readonly SyncTestContext _ctx = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task A_finished_cook_is_computed_when_it_syncs()
    {
        var cook = await AFinishedCookAsync();

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.Equal(TimeSpan.FromHours(12), analytics.Duration);
        Assert.Equal(2, analytics.TimePerKg);
    }

    [Fact]
    public async Task A_running_cook_is_not_computed()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        // A partial cook entering the baseline would read as an unusually fast
        // brisket and drag the user's own normal with it.
        Assert.Null(await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id));
    }

    [Fact]
    public async Task Finishing_a_cook_later_computes_it_then()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        cook.FinishedAt = cook.StartedAt.AddHours(9);
        cook.UpdatedAt = cook.UpdatedAt.AddHours(9);

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, Cooks = [cook] });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.Equal(TimeSpan.FromHours(9), analytics.Duration);
    }

    [Fact]
    public async Task A_cook_that_finished_before_it_started_has_no_duration()
    {
        var cook = await AFinishedCookAsync(durationHours: -3);

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        // The same answer slice 3 gives on the summary screen. If the two
        // disagreed, one cook would read differently in two places.
        Assert.NotNull(analytics);
        Assert.Null(analytics.Duration);
        Assert.Null(analytics.TimePerKg);
    }

    [Fact]
    public async Task The_stall_is_found_from_the_cooks_own_readings()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(12);

        var readings = new[] { (0, 40.0), (2, 66.0), (4, 67.0), (6, 68.0), (8, 90.0) }
            .Select(r => Reading(cook.Id, cook.StartedAt.AddHours(r.Item1), r.Item2))
            .ToList();

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = readings,
        });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.Equal(TimeSpan.FromHours(4), analytics.StallDuration);
    }

    [Fact]
    public async Task A_deleted_reading_is_left_out()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(12);

        var real = Reading(cook.Id, cook.StartedAt.AddHours(1), 60);
        var mistake = Reading(cook.Id, cook.StartedAt.AddHours(2), 999);
        mistake.DeletedAt = cook.StartedAt.AddHours(3);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            TempEntries = [real, mistake],
        });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        // A tombstoned reading is a correction. Counting it would let a typo the
        // user already fixed define their stall and their baseline.
        Assert.NotNull(analytics);
        Assert.Null(analytics.StallDuration);
    }

    [Fact]
    public async Task Pit_readings_are_taken_from_the_window_the_cook_occupied()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(6);

        // Six inside the cook, and one from a different cook on the same rig a
        // week later. Pit readings belong to the rig, not the cook.
        var inside = Enumerable.Range(0, 6)
            .Select(i => PitReading(rig.Id, cook.StartedAt.AddHours(i), 110))
            .ToList();

        var afterwards = PitReading(rig.Id, cook.StartedAt.AddDays(7), 250);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
            PitTempEntries = [.. inside, afterwards],
        });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.Equal(100, analytics.PitStabilityScore);
    }

    [Fact]
    public async Task A_cook_with_no_pit_data_reports_no_score_rather_than_zero()
    {
        var cook = await AFinishedCookAsync();

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        Assert.NotNull(analytics);
        Assert.Null(analytics.PitStabilityScore);
    }

    [Fact]
    public async Task Readings_that_arrive_after_the_cook_still_update_its_numbers()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(12);

        // The cook row lands first. A long cook's readings can spill into the
        // next batch, because a push is capped at 500 records.
        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        var readings = new[] { (0, 40.0), (2, 66.0), (4, 67.0), (6, 68.0), (8, 90.0) }
            .Select(r => Reading(cook.Id, cook.StartedAt.AddHours(r.Item1), r.Item2))
            .ToList();

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, TempEntries = readings });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        // Without this the stall is computed once, from no readings, and never
        // revisited: a paying user is shown a number drawn from a fraction of
        // their cook.
        Assert.NotNull(analytics);
        Assert.Equal(TimeSpan.FromHours(4), analytics.StallDuration);
    }

    [Fact]
    public async Task Pit_readings_that_arrive_later_still_update_the_score()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(6);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        var pit = Enumerable.Range(0, 6)
            .Select(i => PitReading(rig.Id, cook.StartedAt.AddHours(i), 110))
            .ToList();

        await _ctx.PushAsync(new SyncPushRequest { DeviceId = _ctx.Device, PitTempEntries = pit });

        var analytics = await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id);

        // Pit readings belong to the rig, so a later batch of them has to find
        // the finished cooks whose window they fall in.
        Assert.NotNull(analytics);
        Assert.Equal(100, analytics.PitStabilityScore);
    }

    [Fact]
    public async Task Recomputing_replaces_rather_than_duplicates()
    {
        var cook = await AFinishedCookAsync();

        await _ctx.Analytics.RecomputeAsync(_ctx.Account, cook.Id);
        await _ctx.Analytics.RecomputeAsync(_ctx.Account, cook.Id);

        Assert.Equal(1, await _ctx.Db.CookAnalytics.CountAsync(a => a.CookId == cook.Id));
    }

    [Fact]
    public async Task Another_accounts_cook_is_not_computed_or_served()
    {
        var cook = await AFinishedCookAsync();

        await _ctx.Analytics.RecomputeAsync(_ctx.OtherAccount, cook.Id);

        Assert.Null(await _ctx.Analytics.GetAsync(_ctx.OtherAccount, cook.Id));
        Assert.NotNull(await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id));
    }

    [Fact]
    public async Task A_deleted_cook_is_not_computed()
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(6);
        cook.DeletedAt = cook.StartedAt.AddHours(7);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        Assert.Null(await _ctx.Analytics.GetAsync(_ctx.Account, cook.Id));
    }

    [Fact]
    public async Task A_cook_nobody_has_computed_has_no_analytics()
    {
        Assert.Null(await _ctx.Analytics.GetAsync(_ctx.Account, Guid.NewGuid()));
    }

    private async Task<Cook> AFinishedCookAsync(double durationHours = 12)
    {
        var rig = _ctx.ARig();
        var cook = _ctx.ACook(rig.Id);
        cook.FinishedAt = cook.StartedAt.AddHours(durationHours);

        await _ctx.PushAsync(new SyncPushRequest
        {
            DeviceId = _ctx.Device,
            Equipment = [rig],
            Cooks = [cook],
        });

        return cook;
    }

    private static TempEntry Reading(Guid cookId, DateTimeOffset at, double tempC) => new()
    {
        CookId = cookId,
        RecordedAt = at,
        MeatTempC = tempC,
        CreatedAt = at,
        UpdatedAt = at,
    };

    private static PitTempEntry PitReading(Guid equipmentId, DateTimeOffset at, double tempC) => new()
    {
        EquipmentId = equipmentId,
        RecordedAt = at,
        PitTempC = tempC,
        CreatedAt = at,
        UpdatedAt = at,
    };
}
