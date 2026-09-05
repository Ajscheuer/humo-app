using Humo.Core.Data.Records;
using SQLite;

namespace Humo.Core.Data;

/// <summary>
/// Owns the SQLite connection and makes sure the schema exists.
/// <para>
/// SQLite on device is the source of truth during a cook, so nothing here
/// depends on connectivity and no user action waits on the network.
/// </para>
/// </summary>
public interface IHumoDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IConnectionSource
{
    Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class HumoDatabase : IHumoDatabase, IConnectionSource, IAsyncDisposable, IDisposable
{
    private readonly IDatabasePath _path;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public HumoDatabase(IDatabasePath path)
    {
        _path = path;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
        => await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

    async Task<SQLiteAsyncConnection> IConnectionSource.GetConnectionAsync(CancellationToken cancellationToken)
        => await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

    private async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check inside the lock: two screens opening at once must not each
            // create a connection and race the table creation.
            if (_connection is not null)
            {
                return _connection;
            }

            var connection = new SQLiteAsyncConnection(
                _path.DatabaseFilePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

            // CreateTableAsync creates a missing table and ALTERs in columns that
            // were added since. That covers additive schema changes, which is all
            // Humo has needed so far. Anything destructive -- a rename, a type
            // change, a split -- needs the sequential versioned migrations
            // described in architecture.md 5.1, applied in order so a user who
            // skipped versions still lands correctly.
            await connection.CreateTableAsync<EquipmentRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<CookRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<TempEntryRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<PitTempEntryRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<FuelEventRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<EventRecord>().ConfigureAwait(false);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }

        _initLock.Dispose();
    }

    /// <summary>
    /// Synchronous disposal, for hosts that tear their container down that way.
    /// <para>
    /// This class is registered as a singleton, and a DI container throws rather
    /// than guesses when a singleton is <see cref="IAsyncDisposable"/> only — so
    /// leaving this out turns app shutdown into a crash. Prefer
    /// <see cref="DisposeAsync"/>; blocking is acceptable here because the only
    /// caller is shutdown, and sqlite-net's close runs on the thread pool rather
    /// than capturing a context to deadlock on.
    /// </para>
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
