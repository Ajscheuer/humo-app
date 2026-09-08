using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeviceCursorRow>().HasKey(c => new { c.AccountId, c.DeviceId });

        ConfigureSyncedRow<EquipmentRow>(modelBuilder);
        ConfigureSyncedRow<CookRow>(modelBuilder);
        ConfigureSyncedRow<TempEntryRow>(modelBuilder);
        ConfigureSyncedRow<PitTempEntryRow>(modelBuilder);
        ConfigureSyncedRow<FuelEventRow>(modelBuilder);
        ConfigureSyncedRow<EventRow>(modelBuilder);
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
