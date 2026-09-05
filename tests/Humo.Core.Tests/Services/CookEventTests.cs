using Humo.Core.Services;
using Humo.Core.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Tests.Services;

/// <summary>
/// Milestones — wrapped, spritzed, rested. Per cook, unlike fuel: wrapping one
/// brisket says nothing about the ribs beside it on the same fire.
/// </summary>
public class CookEventTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ICookService Service => _db.Service;

    private Task<Cook> ACookAsync(MeatType meatType = MeatType.Brisket, double weightKg = 6)
        => Service.StartCookAsync(new StartCookRequest
        {
            MeatType = meatType,
            WeightKg = weightKg,
        });

    [Fact]
    public async Task A_milestone_records_its_type_and_time()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(6));

        var wrapped = await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });

        Assert.NotEqual(Guid.Empty, wrapped.Id);
        Assert.Equal(cook.Id, wrapped.CookId);
        Assert.Equal(EventType.Wrapped, wrapped.Type);
        Assert.Equal(_db.Clock.UtcNow, wrapped.RecordedAt);
    }

    [Fact]
    public async Task Milestones_are_listed_oldest_first()
    {
        var cook = await ACookAsync();

        foreach (var type in new[] { EventType.Spritzed, EventType.Wrapped, EventType.Rested })
        {
            _db.Clock.Advance(TimeSpan.FromHours(2));
            await Service.LogEventAsync(new LogEventRequest { CookId = cook.Id, Type = type });
        }

        Assert.Equal(
            [EventType.Spritzed, EventType.Wrapped, EventType.Rested],
            (await Service.GetEventsAsync(cook.Id)).Select(e => e.Type));
    }

    [Fact]
    public async Task A_back_dated_milestone_lands_in_the_right_place_in_the_story()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(6));

        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });

        // Remembering an hour later that you spritzed before wrapping.
        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Spritzed,
            RecordedAt = _db.Clock.UtcNow - TimeSpan.FromHours(1),
        });

        Assert.Equal(
            [EventType.Spritzed, EventType.Wrapped],
            (await Service.GetEventsAsync(cook.Id)).Select(e => e.Type));
    }

    [Fact]
    public async Task Milestones_belong_to_one_cook_even_on_a_shared_fire()
    {
        var rig = await Service.GetOrCreateDefaultEquipmentAsync();
        var brisket = await Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            EquipmentId = rig.Id,
        });
        var ribs = await Service.StartCookAsync(new StartCookRequest
        {
            MeatType = MeatType.PorkRibs,
            WeightKg = 1.5,
            EquipmentId = rig.Id,
        });

        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = brisket.Id,
            Type = EventType.Wrapped,
        });

        // The opposite of fuel: one fire, but wrapping the brisket says nothing
        // about the ribs.
        Assert.Single(await Service.GetEventsAsync(brisket.Id));
        Assert.Empty(await Service.GetEventsAsync(ribs.Id));
    }

    [Fact]
    public async Task A_milestone_counts_as_activity_on_the_cook()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(8));

        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });

        // Otherwise a cook logging only milestones for hours looks idle, gets
        // prompted as stale, and is eventually auto-finished while the user is
        // standing at the smoker.
        var stored = await _db.Cooks.GetAsync(cook.Id);
        Assert.Equal(_db.Clock.UtcNow, stored!.LastActivityAt);
    }

    [Fact]
    public async Task A_back_dated_milestone_does_not_drag_activity_backwards()
    {
        var cook = await ACookAsync();
        _db.Clock.Advance(TimeSpan.FromHours(8));
        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
        });

        var activityAfterWrap = (await _db.Cooks.GetAsync(cook.Id))!.LastActivityAt;

        await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Spritzed,
            RecordedAt = _db.Clock.UtcNow - TimeSpan.FromHours(3),
        });

        var stored = await _db.Cooks.GetAsync(cook.Id);
        Assert.Equal(activityAfterWrap, stored!.LastActivityAt);
    }

    [Fact]
    public async Task A_milestone_cannot_be_logged_against_a_finished_cook()
    {
        var cook = await ACookAsync();
        await Service.FinishCookAsync(cook.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.LogEventAsync(new LogEventRequest
            {
                CookId = cook.Id,
                Type = EventType.Rested,
            }));
    }

    [Fact]
    public async Task A_milestone_against_a_cook_that_does_not_exist_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.LogEventAsync(new LogEventRequest
            {
                CookId = Guid.NewGuid(),
                Type = EventType.Wrapped,
            }));
    }

    [Fact]
    public async Task A_note_is_trimmed_and_a_blank_note_is_dropped()
    {
        var cook = await ACookAsync();

        var withNote = await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Other,
            Note = "  Bumped the pit to 135  ",
        });
        var blank = await Service.LogEventAsync(new LogEventRequest
        {
            CookId = cook.Id,
            Type = EventType.Wrapped,
            Note = "   ",
        });

        Assert.Equal("Bumped the pit to 135", withNote.Note);
        Assert.Null(blank.Note);
    }

    [Fact]
    public async Task The_same_milestone_can_be_logged_more_than_once()
    {
        var cook = await ACookAsync();

        // Spritzing every 45 minutes is exactly how a cook goes.
        for (var i = 0; i < 3; i++)
        {
            _db.Clock.Advance(TimeSpan.FromMinutes(45));
            await Service.LogEventAsync(new LogEventRequest
            {
                CookId = cook.Id,
                Type = EventType.Spritzed,
            });
        }

        Assert.Equal(3, (await Service.GetEventsAsync(cook.Id)).Count);
    }

    [Fact]
    public async Task A_cook_with_no_milestones_reads_as_empty_rather_than_failing()
    {
        var cook = await ACookAsync();

        Assert.Empty(await Service.GetEventsAsync(cook.Id));
    }
}
