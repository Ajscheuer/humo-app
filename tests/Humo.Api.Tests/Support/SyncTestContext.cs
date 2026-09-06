using Humo.Api.Data;
using Humo.Api.Sync;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Support;

/// <summary>
/// A real database and a real <see cref="SyncService"/>.
/// <para>
/// SQLite rather than the in-memory provider: sync correctness is about what a
/// database actually does with keys, ordering and repeated writes, and the
/// in-memory provider would accept things Azure SQL will not.
/// </para>
/// </summary>
internal sealed class SyncTestContext : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public SyncTestContext(DateTimeOffset? now = null)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HumoDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new HumoDbContext(options);
        Db.Database.EnsureCreated();

        Time = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 3, 14, 6, 0, 0, TimeSpan.Zero));
        Service = new SyncService(Db, Time);
    }

    public HumoDbContext Db { get; }

    public FakeTimeProvider Time { get; }

    public ISyncService Service { get; }

    public Guid Account { get; } = Guid.NewGuid();

    public Guid OtherAccount { get; } = Guid.NewGuid();

    public Guid Device { get; } = Guid.NewGuid();

    public Guid SecondDevice { get; } = Guid.NewGuid();

    public Task<SyncPushResponse> PushAsync(SyncPushRequest request, Guid? account = null)
        => Service.PushAsync(account ?? Account, request);

    public Task<SyncPullResponse> PullAsync(long cursor = 0, Guid? device = null, Guid? account = null)
        => Service.PullAsync(account ?? Account, device ?? Device, cursor);

    /// <summary>A rig, as the client would send it.</summary>
    public Equipment ARig(string name = "Old Country Brazos", DateTimeOffset? updatedAt = null)
    {
        var at = updatedAt ?? Time.GetUtcNow();
        return new Equipment
        {
            Name = name,
            Type = EquipmentType.Offset,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public Cook ACook(Guid equipmentId, DateTimeOffset? updatedAt = null)
    {
        var at = updatedAt ?? Time.GetUtcNow();
        return new Cook
        {
            EquipmentId = equipmentId,
            PitType = EquipmentType.Offset,
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            StartedAt = at,
            LastActivityAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public TempEntry AReading(Guid cookId, double meatTempC = 68, DateTimeOffset? updatedAt = null)
    {
        var at = updatedAt ?? Time.GetUtcNow();
        return new TempEntry
        {
            CookId = cookId,
            RecordedAt = at,
            MeatTempC = meatTempC,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public FuelEvent AFuelLoad(Guid equipmentId, DateTimeOffset? updatedAt = null)
    {
        var at = updatedAt ?? Time.GetUtcNow();
        return new FuelEvent
        {
            EquipmentId = equipmentId,
            WoodType = WoodType.Oak,
            Form = FuelForm.Split,
            SizeClass = SizeClass.Medium,
            Count = 1,
            RecordedAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>A clock the test drives, so receipt times and skew are deterministic.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
