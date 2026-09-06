using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Humo.Api.Data;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// <para>
/// Without this, adding a migration starts the whole host, which wants a real
/// Azure SQL connection string and an identity provider. A migration is a
/// description of the model, not of any particular database, so the placeholder
/// below is never connected to — <c>dotnet ef</c> only needs the SQL Server
/// provider loaded to know how to render the DDL.
/// </para>
/// </summary>
public sealed class HumoDbContextFactory : IDesignTimeDbContextFactory<HumoDbContext>
{
    public HumoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HumoDbContext>()
            .UseSqlServer("Server=design-time;Database=Humo;Trusted_Connection=True;")
            .Options;

        return new HumoDbContext(options);
    }
}
