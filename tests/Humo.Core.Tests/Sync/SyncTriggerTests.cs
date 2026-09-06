using Humo.Core.Sync;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Humo.Core.Tests.Sync;

public class SyncTriggerTests
{
    private readonly ISyncService _service = Substitute.For<ISyncService>();
    private readonly ISyncFailureLog _log = new RecordingFailureLog();

    [Fact]
    public async Task Requesting_a_sync_runs_one()
    {
        var completed = new TaskCompletionSource();
        _service.SyncAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            completed.TrySetResult();
            return Task.FromResult(SyncResult.Of(SyncOutcome.Completed));
        });

        new SyncTrigger(_service, _log).RequestSync();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Requesting_a_sync_does_not_wait_for_it()
    {
        var release = new TaskCompletionSource();
        _service.SyncAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await release.Task;
                return SyncResult.Of(SyncOutcome.Completed);
            });

        var trigger = new SyncTrigger(_service, _log);

        // If this blocked, an app resuming with a season of cooks to upload
        // would freeze on the splash screen.
        trigger.RequestSync();

        Assert.Null(trigger.LastResult);
        release.SetResult();
    }

    [Fact]
    public async Task A_sync_that_throws_does_not_take_the_app_down()
    {
        _service.SyncAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the database is locked"));

        var trigger = new SyncTrigger(_service, _log);

        trigger.RequestSync();

        // An unobserved task exception on a background thread crashes on both
        // platforms, and a cook in progress is worth more than a sync round.
        await WaitFor(() => _log.Last is not null);
        Assert.IsType<InvalidOperationException>(_log.Last);
    }

    [Fact]
    public async Task A_swallowed_failure_is_recorded_rather_than_lost()
    {
        _service.SyncAsync(Arg.Any<CancellationToken>())
            .Throws(new TimeoutException());

        new SyncTrigger(_service, _log).RequestSync();

        await WaitFor(() => _log.Last is not null);
        Assert.IsType<TimeoutException>(_log.Last);
    }

    [Fact]
    public async Task The_last_result_is_available_once_the_round_finishes()
    {
        _service.SyncAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncResult { Outcome = SyncOutcome.Completed, Pushed = 3 }));

        var trigger = new SyncTrigger(_service, _log);
        trigger.RequestSync();

        await WaitFor(() => trigger.LastResult is not null);
        Assert.Equal(3, trigger.LastResult!.Pushed);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The background sync did not finish within five seconds.");
    }

    /// <summary>A substitute would be racy to assert on; this is just a field.</summary>
    private sealed class RecordingFailureLog : ISyncFailureLog
    {
        public Exception? Last { get; private set; }

        public void Record(Exception exception) => Last = exception;
    }
}
