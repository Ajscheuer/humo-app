using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Humo.Api.Data;

/// <summary>
/// The server's view of synced data.
/// <para>
/// Deliberately separate types from <c>Humo.Shared</c>'s entities, for the same
/// reason the device has its own persistence records: the wire contract and the
/// storage shape change for different reasons, and a column can be indexed or
/// renamed here without touching what the app and API agree on.
/// </para>
/// </summary>
public sealed class HumoDbContext : DbContext
{
    public HumoDbContext(DbContextOptions<HumoDbContext> options)
        : base(options)
    {
    }

    public DbSet<EquipmentRow> Equipment => Set<EquipmentRow>();

    public DbSet<CookRow> Cooks => Set<CookRow>();

    public DbSet<TempEntryRow> TempEntries => Set<TempEntryRow>();

    public DbSet<PitTempEntryRow> PitTempEntries => Set<PitTempEntryRow>();

    public DbSet<FuelEventRow> FuelEvents => Set<FuelEventRow>();

    public DbSet<EventRow> Events => Set<EventRow>();

    public DbSet<DeviceCursorRow> DeviceCursors => Set<DeviceCursorRow>();

    public DbSet<AccountSequenceRow> AccountSequences => Set<AccountSequenceRow>();

    public DbSet<EntitlementRow> Entitlements => Set<EntitlementRow>();

    public DbSet<CookAnalyticsRow> CookAnalytics => Set<CookAnalyticsRow>();

    public DbSet<UserBaselineRow> UserBaselines => Set<UserBaselineRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeviceCursorRow>().HasKey(c => new { c.AccountId, c.DeviceId });

        // Derived rows, keyed by what they describe. Account first for the same
        // reason the synced rows are: a cook id is only unique within the
        // account that minted it.
        modelBuilder.Entity<CookAnalyticsRow>().HasKey(a => new { a.AccountId, a.CookId });

        // Every baseline read is "this account, this meat on this rig", so that
        // is the index, and one row per metric within it.
        modelBuilder.Entity<UserBaselineRow>()
            .HasKey(b => new { b.AccountId, b.MeatType, b.EquipmentId, b.Metric });

        // The baseline refresh reads every analytics row for one pair.
        modelBuilder.Entity<CookAnalyticsRow>()
            .HasIndex(a => new { a.AccountId, a.MeatType, a.EquipmentId });

        ApplySqliteTimestampConversion(modelBuilder, Database.ProviderName);

        ConfigureSyncedRow<EquipmentRow>(modelBuilder);
        ConfigureSyncedRow<CookRow>(modelBuilder);
        ConfigureSyncedRow<TempEntryRow>(modelBuilder);
        ConfigureSyncedRow<PitTempEntryRow>(modelBuilder);
        ConfigureSyncedRow<FuelEventRow>(modelBuilder);
        ConfigureSyncedRow<EventRow>(modelBuilder);
    }

    /// <summary>
    /// Stores every timestamp as UTC ticks when running on SQLite.
    /// <para>
    /// SQLite has no date type, and EF's default text storage for
    /// <see cref="DateTimeOffset"/> is not sortable across offsets — so the
    /// provider refuses to translate <c>&gt;=</c> and <c>&lt;=</c> on one at all.
    /// Analytics reads pit readings for the window a cook occupied, so without
    /// this the tests cannot exercise a time range that Azure SQL handles
    /// natively, which is exactly the kind of gap the SQLite-backed tests exist
    /// to close.
    /// </para>
    /// <para>
    /// Lossless here because every instant Humo stores is UTC — a rule the
    /// entities state and the tests check. On SQL Server nothing changes: the
    /// columns stay real <c>datetimeoffset</c>, readable by anyone debugging
    /// production.
    /// </para>
    /// </summary>
    private static void ApplySqliteTimestampConversion(ModelBuilder modelBuilder, string? providerName)
    {
        // By provider name rather than the Sqlite extension method, so the API
        // does not take a package reference for something only its tests use.
        // EF keys its model cache on the provider, so the two shapes never mix.
        if (providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var toTicks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        var toNullableTicks = new ValueConverter<DateTimeOffset?, long?>(
            value => value == null ? null : value.Value.UtcTicks,
            ticks => ticks == null ? null : new DateTimeOffset(ticks.Value, TimeSpan.Zero));

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(toTicks);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(toNullableTicks);
            }
        }
    }

    private static void ConfigureSyncedRow<TRow>(ModelBuilder modelBuilder)
        where TRow : SyncedRow
    {
        // The account is part of the key, not just a column the queries remember
        // to filter on. Record ids are client-generated, so an id is only unique
        // within the account that minted it; keying on the id alone would let one
        // account's push collide with another's row, and the database rather than
        // the merge rules would decide what happened.
        modelBuilder.Entity<TRow>().HasKey(r => new { r.AccountId, r.Id });

        // Every pull is "this account, everything after this sequence", so that
        // pair is the index every read depends on. Without it, incremental sync
        // degrades to a table scan as soon as one account has a season of cooks.
        modelBuilder.Entity<TRow>().HasIndex(r => new { r.AccountId, r.Sequence });
    }
}
