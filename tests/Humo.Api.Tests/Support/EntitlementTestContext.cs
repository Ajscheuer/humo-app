using Humo.Api.Data;
using Humo.Api.Entitlements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Humo.Api.Tests.Support;

/// <summary>
/// A real database and a real <see cref="EntitlementService"/>, on a clock the
/// test drives — expiry is the whole point of this service, and it cannot be
/// tested against a clock that moves on its own.
/// </summary>
internal sealed class EntitlementTestContext : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public EntitlementTestContext()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        Db = new HumoDbContext(new DbContextOptionsBuilder<HumoDbContext>()
            .UseSqlite(_connection)
            .Options);

        Db.Database.EnsureCreated();

        Time = new FakeTimeProvider(Now);
        Options = new EntitlementOptions();

        // A live options object rather than a snapshot, so a test can change a
        // setting the way App Service configuration would.
        Service = new EntitlementService(Db, Time, new LiveOptions(Options));
    }

    public DateTimeOffset Now { get; } = new(2026, 3, 14, 6, 0, 0, TimeSpan.Zero);

    public HumoDbContext Db { get; }

    public FakeTimeProvider Time { get; }

    public EntitlementOptions Options { get; }

    public IEntitlementService Service { get; }

    public Guid Account { get; } = Guid.NewGuid();

    public Guid OtherAccount { get; } = Guid.NewGuid();

    /// <summary>A purchase of Pro, as the store would report it.</summary>
    public StoreEntitlementEvent APurchase(
        string eventId = "evt-purchase",
        DateTimeOffset? occurredAt = null,
        IReadOnlyList<string>? entitlements = null,
        DateTimeOffset? expiresAt = null,
        Guid? account = null) => new()
    {
        AccountId = account ?? Account,
        EventId = eventId,
        OccurredAt = occurredAt ?? Now,
        ActiveEntitlementIds = entitlements ?? ["pro"],
        ExpiresAt = expiresAt,
        StoreSubscriberId = (account ?? Account).ToString(),
        ProductId = "humo_pro_monthly",
    };

    public Task<EntitlementRow> RowAsync(Guid? account = null)
        => Db.Entitlements.SingleAsync(e => e.AccountId == (account ?? Account));

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Reads the same options instance every time, so edits take effect.</summary>
    private sealed class LiveOptions : IOptions<EntitlementOptions>
    {
        public LiveOptions(EntitlementOptions value) => Value = value;

        public EntitlementOptions Value { get; }
    }
}
