using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Services;

public class CookServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static StartCookRequest ABrisket(double weightKg = 6.0) => new()
    {
        MeatType = MeatType.Brisket,
        WeightKg = weightKg,
    };

    // ---- Starting a cook ---------------------------------------------------

    [Fact]
    public async Task Starting_a_cook_creates_the_default_rig_on_first_use()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        var equipment = await _db.Equipment.GetAsync(cook.EquipmentId);
        Assert.NotNull(equipment);
        Assert.Equal(CookService.DefaultEquipmentName, equipment.Name);
    }

    [Fact]
    public async Task A_second_cook_reuses_the_same_rig_rather_than_making_another()
    {
        var first = await _db.Service.StartCookAsync(ABrisket());
        await _db.Service.FinishCookAsync(first.Id);
        var second = await _db.Service.StartCookAsync(ABrisket());

        Assert.Equal(first.EquipmentId, second.EquipmentId);
        Assert.Single(await _db.Equipment.GetAllAsync());
    }

    [Fact]
    public async Task The_cook_snapshots_the_pit_type_rather_than_pointing_at_it()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        var equipment = (await _db.Equipment.GetAsync(cook.EquipmentId))!;

        // Editing the rig must not rewrite what past cooks were run on: the fire
        // model groups by pit type and analytics compare across rigs.
        equipment.Type = EquipmentType.Kamado;
        await _db.Equipment.SaveAsync(equipment);

        var reloaded = (await _db.Cooks.GetAsync(cook.Id))!;
        Assert.Equal(EquipmentType.Offset, reloaded.PitType);
    }

    [Fact]
    public async Task A_started_cook_is_unfinished_and_its_activity_is_its_start()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        Assert.False(cook.IsFinished);
        Assert.Null(cook.FinishedAt);
        Assert.Null(cook.FinishReason);
        Assert.Equal(cook.StartedAt, cook.LastActivityAt);
        Assert.Equal(_db.Clock.UtcNow, cook.StartedAt);
    }

    [Fact]
    public async Task Timestamps_are_stored_in_UTC()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        var reloaded = (await _db.Cooks.GetAsync(cook.Id))!;

        Assert.Equal(TimeSpan.Zero, reloaded.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, reloaded.CreatedAt.Offset);
    }

    [Fact]
    public async Task Each_cook_gets_its_own_client_generated_id()
    {
        var first = await _db.Service.StartCookAsync(ABrisket());
        await _db.Service.FinishCookAsync(first.Id);
        var second = await _db.Service.StartCookAsync(ABrisket());

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_cook_cannot_start_without_a_sensible_weight(double weightKg)
    {
        // Weight feeds the fire model's thermal load for the whole rig, so a
        // missing or nonsensical value would corrupt predictions for every cook
        // sharing that fire -- not just this one's time-per-kg.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _db.Service.StartCookAsync(ABrisket(weightKg)));
    }

    [Fact]
    public async Task Free_text_is_kept_only_when_the_meat_type_is_Other()
    {
        var other = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Other,
            MeatTypeOther = "Goat shoulder",
            WeightKg = 3,
        });
        Assert.Equal("Goat shoulder", other.MeatTypeOther);

        var brisket = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            MeatTypeOther = "ignored",
            WeightKg = 6,
        });

        // Otherwise a cook could claim to be a brisket and carry contradictory
        // free text, which would then have to be reconciled by every reader.
        Assert.Null(brisket.MeatTypeOther);
    }

    [Fact]
    public async Task A_target_temperature_is_optional()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        // A parrilla cook working by feel has no target temp.
        Assert.Null(cook.TargetInternalTempC);
    }

    [Fact]
    public async Task Starting_against_a_rig_that_does_not_exist_fails_loudly()
    {
        var request = new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = Guid.NewGuid(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Service.StartCookAsync(request));
    }

    // ---- Logging temperatures ----------------------------------------------

    [Fact]
    public async Task A_meat_reading_belongs_to_the_cook_and_a_pit_reading_to_the_rig()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 60,
            PitTempC = 110,
        });

        var meat = await _db.TempEntries.GetForCookAsync(cook.Id);
        var pit = await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, cook.StartedAt.AddDays(-1), cook.StartedAt.AddDays(1));

        // One sheet, two records: there is one fire and it has one temperature,
        // so two cooks on a rig cannot contradict each other about it.
        Assert.Equal(60, Assert.Single(meat).MeatTempC);
        Assert.Equal(110, Assert.Single(pit).PitTempC);
        Assert.Equal(cook.EquipmentId, pit[0].EquipmentId);
    }

    [Fact]
    public async Task A_pit_reading_is_optional()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id,
            MeatTempC = 60,
        });

        var pit = await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, cook.StartedAt.AddDays(-1), cook.StartedAt.AddDays(1));
        Assert.Empty(pit);
    }

    [Fact]
    public async Task Two_cooks_on_one_rig_share_the_pit_history()
    {
        var brisket = await _db.Service.StartCookAsync(ABrisket());
        var ribs = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkRibs,
            WeightKg = 1.5,
            EquipmentId = brisket.EquipmentId,
        });

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = brisket.Id, MeatTempC = 60, PitTempC = 110,
        });
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = ribs.Id, MeatTempC = 55, PitTempC = 112,
        });

        var pit = await _db.PitTempEntries.GetForEquipmentAsync(
            brisket.EquipmentId, brisket.StartedAt.AddDays(-1), brisket.StartedAt.AddDays(1));

        // Both readings describe the same fire, so both must be visible to it.
        Assert.Equal(2, pit.Count);

        // The meat readings stay separate -- each piece of meat has its own
        // internal temperature.
        Assert.Single(await _db.TempEntries.GetForCookAsync(brisket.Id));
        Assert.Single(await _db.TempEntries.GetForCookAsync(ribs.Id));
    }

    [Fact]
    public async Task A_reading_defaults_to_now_but_can_be_back_dated()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(2));

        var defaulted = await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 60,
        });
        Assert.Equal(_db.Clock.UtcNow, defaulted.RecordedAt);

        // Cooks routinely log a reading minutes after taking it, and a wrong
        // timestamp distorts stall detection and the fire model alike.
        var takenEarlier = _db.Clock.UtcNow.AddMinutes(-25);
        var backDated = await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 58, RecordedAt = takenEarlier,
        });
        Assert.Equal(takenEarlier, backDated.RecordedAt);
    }

    [Fact]
    public async Task Logging_a_reading_moves_the_cook_activity_forward()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(3));

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 70,
        });

        var reloaded = (await _db.Cooks.GetAsync(cook.Id))!;
        Assert.Equal(_db.Clock.UtcNow, reloaded.LastActivityAt);
    }

    [Fact]
    public async Task A_back_dated_reading_does_not_drag_activity_backwards()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(5));

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 75,
        });
        var afterLatest = (await _db.Cooks.GetAsync(cook.Id))!.LastActivityAt;

        // Catching up on an older reading must not make the cook look idle: the
        // stale-cook rules key off this field, and an hours-old value would put a
        // live cook closer to being auto-finished.
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 70, RecordedAt = cook.StartedAt.AddHours(1),
        });

        Assert.Equal(afterLatest, (await _db.Cooks.GetAsync(cook.Id))!.LastActivityAt);
    }

    [Fact]
    public async Task Readings_come_back_oldest_first_however_they_were_entered()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(4));

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 75,
        });
        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 62, RecordedAt = cook.StartedAt.AddHours(1),
        });

        var entries = await _db.Service.GetTemperaturesAsync(cook.Id);
        Assert.Equal([62d, 75d], entries.Select(e => e.MeatTempC));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_non_finite_reading_is_rejected(double value)
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _db.Service.LogTemperatureAsync(new LogTemperatureRequest
            {
                CookId = cook.Id, MeatTempC = value,
            }));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _db.Service.LogTemperatureAsync(new LogTemperatureRequest
            {
                CookId = cook.Id, MeatTempC = 60, PitTempC = value,
            }));
    }

    [Fact]
    public async Task Logging_against_an_unknown_cook_fails_loudly()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Service.LogTemperatureAsync(new LogTemperatureRequest
            {
                CookId = Guid.NewGuid(), MeatTempC = 60,
            }));
    }

    [Fact]
    public async Task A_finished_cook_accepts_no_more_readings()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        await _db.Service.FinishCookAsync(cook.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Service.LogTemperatureAsync(new LogTemperatureRequest
            {
                CookId = cook.Id, MeatTempC = 90,
            }));
    }

    [Fact]
    public async Task Sub_zero_ambient_survives_a_round_trip()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        await _db.Service.LogTemperatureAsync(new LogTemperatureRequest
        {
            CookId = cook.Id, MeatTempC = 5, PitTempC = 105, AmbientTempC = -12.5,
        });

        var pit = await _db.PitTempEntries.GetForEquipmentAsync(
            cook.EquipmentId, cook.StartedAt.AddDays(-1), cook.StartedAt.AddDays(1));

        // Winter cooks are ordinary; a sign error here would only show in January.
        Assert.Equal(-12.5, Assert.Single(pit).AmbientTempC);
    }

    // ---- Finishing ---------------------------------------------------------

    [Fact]
    public async Task Finishing_records_the_end_time_and_that_a_person_did_it()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(12));

        var finished = await _db.Service.FinishCookAsync(cook.Id, rating: 4, notes: "Good bark");

        Assert.True(finished.IsFinished);
        Assert.Equal(_db.Clock.UtcNow, finished.FinishedAt);

        // Manual, not AutoFinished: the distinction is what keeps inferred end
        // times out of duration baselines later.
        Assert.Equal(CookFinishReason.Manual, finished.FinishReason);
        Assert.Equal(4, finished.Rating);
        Assert.Equal("Good bark", finished.Notes);
    }

    [Fact]
    public async Task Finishing_without_a_rating_leaves_it_unrated()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        var finished = await _db.Service.FinishCookAsync(cook.Id);

        Assert.Null(finished.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task A_rating_outside_one_to_five_is_rejected(int rating)
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _db.Service.FinishCookAsync(cook.Id, rating));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task The_rating_boundaries_are_accepted(int rating)
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        var finished = await _db.Service.FinishCookAsync(cook.Id, rating);

        Assert.Equal(rating, finished.Rating);
    }

    [Fact]
    public async Task A_cook_cannot_be_finished_twice()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        await _db.Service.FinishCookAsync(cook.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Service.FinishCookAsync(cook.Id));
    }

    [Fact]
    public async Task Finishing_an_unknown_cook_fails_loudly()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Service.FinishCookAsync(Guid.NewGuid()));
    }

    // ---- The active cook ---------------------------------------------------

    [Fact]
    public async Task There_is_no_active_cook_before_one_starts()
    {
        Assert.Null(await _db.Service.GetActiveCookAsync());
    }

    [Fact]
    public async Task A_finished_cook_is_no_longer_active()
    {
        var cook = await _db.Service.StartCookAsync(ABrisket());
        await _db.Service.FinishCookAsync(cook.Id);

        Assert.Null(await _db.Service.GetActiveCookAsync());
    }

    [Fact]
    public async Task The_active_cook_is_the_most_recently_started_unfinished_one()
    {
        var older = await _db.Service.StartCookAsync(ABrisket());
        _db.Clock.Advance(TimeSpan.FromHours(1));
        var newer = await _db.Service.StartCookAsync(ABrisket());

        var active = await _db.Service.GetActiveCookAsync();

        Assert.Equal(newer.Id, active?.Id);
        Assert.NotEqual(older.Id, active?.Id);
    }

    [Fact]
    public async Task A_cook_survives_a_round_trip_through_the_database_intact()
    {
        var started = await _db.Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkButt,
            WeightKg = 4.2,
            TargetInternalTempC = 95.5,
            AmbientTempC = 8.0,
            Notes = "Overnight",
        });

        var reloaded = (await _db.Cooks.GetAsync(started.Id))!;

        Assert.Equal(started.Id, reloaded.Id);
        Assert.Equal(MeatType.PorkButt, reloaded.MeatType);
        Assert.Equal(4.2, reloaded.WeightKg);
        Assert.Equal(95.5, reloaded.TargetInternalTempC);
        Assert.Equal(8.0, reloaded.AmbientTempC);
        Assert.Equal("Overnight", reloaded.Notes);
        Assert.Equal(started.StartedAt, reloaded.StartedAt);
        Assert.Null(reloaded.FinishReason);
    }
}
