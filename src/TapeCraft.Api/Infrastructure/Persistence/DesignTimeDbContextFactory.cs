using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace TapeCraft.Api.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without a database, DATABASE_URL or key ring.
/// The throwaway key ring never encrypts anything, because building a migration stores no data.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TapeCraftDbContext>
{
    public TapeCraftDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TapeCraftDbContext>();
        PersistenceSetup.Configure(options, NpgsqlDataSource.Create("Host=localhost"));
        return new TapeCraftDbContext(options.Options, new EphemeralDataProtectionProvider());
    }
}
