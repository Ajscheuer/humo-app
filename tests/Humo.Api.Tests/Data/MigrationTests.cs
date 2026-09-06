using Humo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Humo.Api.Tests.Data;

public class MigrationTests
{
    [Fact]
    public void The_migrations_are_up_to_date_with_the_model()
    {
        using var db = new HumoDbContextFactory().CreateDbContext([]);

        // Migrations are applied on deploy (architecture.md decision 6), so a
        // model change nobody generated a migration for is not caught by any
        // test that talks to a database -- it surfaces as a column missing in
        // production. This asks EF the question directly, with no connection.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Every_synced_table_is_keyed_by_account_and_id()
    {
        using var db = new HumoDbContextFactory().CreateDbContext([]);

        var syncedRows = db.Model.GetEntityTypes()
            .Where(t => typeof(SyncedRow).IsAssignableFrom(t.ClrType))
            .ToList();

        Assert.Equal(6, syncedRows.Count);

        // Structural isolation: an id is only unique within the account that
        // minted it, so a table keyed on the id alone would let one account's
        // push collide with another's row.
        Assert.All(syncedRows, t =>
        {
            var key = t.FindPrimaryKey();
            Assert.NotNull(key);
            Assert.Equal(
                [nameof(SyncedRow.AccountId), nameof(SyncedRow.Id)],
                key.Properties.Select(p => p.Name));
        });
    }
}
