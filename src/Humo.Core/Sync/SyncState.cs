using System.Globalization;
using Humo.Core.Settings;

namespace Humo.Core.Sync;

/// <summary>
/// This device's identity in the stream, and how far it has read.
/// </summary>
public interface ISyncState
{
    /// <summary>
    /// A stable id for this installation, minted on first use. Not the account
    /// and not the hardware: two people signing in on one phone share it, and a
    /// reinstall gets a new one, which costs a single full pull and nothing else.
    /// </summary>
    Guid DeviceId { get; }

    /// <summary>The last sequence number this device has seen, per account.</summary>
    long GetCursor(Guid accountId);

    void SetCursor(Guid accountId, long cursor);
}

internal sealed class SyncState : ISyncState
{
    private const string DeviceIdKey = "sync.deviceId";
    private const string CursorKeyPrefix = "sync.cursor.";

    private readonly IAppPreferences _preferences;

    public SyncState(IAppPreferences preferences) => _preferences = preferences;

    public Guid DeviceId
    {
        get
        {
            var stored = _preferences.GetString(DeviceIdKey);

            if (Guid.TryParse(stored, out var parsed) && parsed != Guid.Empty)
            {
                return parsed;
            }

            // Also covers a corrupted value. Minting a fresh one costs a full
            // re-pull; refusing to sync until someone clears a preference does
            // not have a recovery path at all.
            var minted = Guid.NewGuid();
            _preferences.SetString(DeviceIdKey, minted.ToString());
            return minted;
        }
    }

    // Per account: signing out of one account and into another on the same phone
    // must not carry a cursor across, or the second account would start partway
    // through a stream it has never read.
    public long GetCursor(Guid accountId)
        => long.TryParse(
            _preferences.GetString(CursorKeyPrefix + accountId),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var cursor) && cursor >= 0
            ? cursor
            : 0;

    public void SetCursor(Guid accountId, long cursor)
    {
        if (cursor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor), cursor, "A cursor is never negative.");
        }

        // Never backwards. A pull that returned nothing hands back the cursor it
        // was given, and a late response from an earlier request must not rewind
        // a device that has already read further.
        if (cursor <= GetCursor(accountId))
        {
            return;
        }

        _preferences.SetString(
            CursorKeyPrefix + accountId,
            cursor.ToString(CultureInfo.InvariantCulture));
    }
}
