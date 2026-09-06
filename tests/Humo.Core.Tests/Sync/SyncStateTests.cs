using Humo.Core.Sync;
using Humo.Core.Tests.Support;

namespace Humo.Core.Tests.Sync;

public class SyncStateTests
{
    private readonly InMemoryPreferences _preferences = new();

    [Fact]
    public void A_device_id_survives_a_relaunch()
    {
        var first = new SyncState(_preferences).DeviceId;

        // A second instance over the same preferences is the next app launch.
        Assert.Equal(first, new SyncState(_preferences).DeviceId);
    }

    [Fact]
    public void A_device_id_is_stable_within_one_run()
    {
        var state = new SyncState(_preferences);

        Assert.Equal(state.DeviceId, state.DeviceId);
    }

    [Fact]
    public void A_device_id_is_never_empty()
    {
        Assert.NotEqual(Guid.Empty, new SyncState(_preferences).DeviceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_corrupt_stored_device_id_is_replaced_rather_than_used(string stored)
    {
        _preferences.SetString("sync.deviceId", stored);

        var deviceId = new SyncState(_preferences).DeviceId;

        // A full re-pull is a recoverable cost. Refusing to sync until somebody
        // clears a preference is not a recovery path at all.
        Assert.NotEqual(Guid.Empty, deviceId);
        Assert.Equal(deviceId, new SyncState(_preferences).DeviceId);
    }

    [Fact]
    public void A_cursor_starts_at_the_beginning_of_the_stream()
    {
        Assert.Equal(0, new SyncState(_preferences).GetCursor(Guid.NewGuid()));
    }

    [Fact]
    public void A_cursor_survives_a_relaunch()
    {
        var account = Guid.NewGuid();
        new SyncState(_preferences).SetCursor(account, 42);

        Assert.Equal(42, new SyncState(_preferences).GetCursor(account));
    }

    [Fact]
    public void Two_accounts_on_one_phone_keep_separate_cursors()
    {
        var state = new SyncState(_preferences);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        state.SetCursor(first, 90);

        // Otherwise the second account would start partway through a stream it
        // has never read, and silently miss everything before that point.
        Assert.Equal(0, state.GetCursor(second));
        Assert.Equal(90, state.GetCursor(first));
    }

    [Fact]
    public void A_cursor_never_goes_backwards()
    {
        var state = new SyncState(_preferences);
        var account = Guid.NewGuid();

        state.SetCursor(account, 90);
        state.SetCursor(account, 10);

        Assert.Equal(90, state.GetCursor(account));
    }

    [Fact]
    public void Setting_the_same_cursor_again_is_harmless()
    {
        var state = new SyncState(_preferences);
        var account = Guid.NewGuid();

        state.SetCursor(account, 7);
        state.SetCursor(account, 7);

        Assert.Equal(7, state.GetCursor(account));
    }

    [Fact]
    public void A_negative_cursor_is_refused()
    {
        var state = new SyncState(_preferences);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetCursor(Guid.NewGuid(), -1));
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("-5")]
    [InlineData("")]
    public void A_corrupt_stored_cursor_reads_as_the_beginning(string stored)
    {
        var account = Guid.NewGuid();
        _preferences.SetString("sync.cursor." + account, stored);

        // Re-pulling from zero is idempotent, so starting over is safe. Trusting
        // a garbage value would skip records permanently.
        Assert.Equal(0, new SyncState(_preferences).GetCursor(account));
    }
}
