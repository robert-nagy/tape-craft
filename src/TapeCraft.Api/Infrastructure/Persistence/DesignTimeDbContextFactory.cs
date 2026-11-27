using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace TapeCraft.Api.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a database or DATABASE_URL.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TapeCraftDbContext>
{
    public TapeCraftDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TapeCraftDbContext>();
        PersistenceSetup.Configure(options, NpgsqlDataSource.Create("Host=localhost"));
        return new TapeCraftDbContext(options.Options);
    }
}
