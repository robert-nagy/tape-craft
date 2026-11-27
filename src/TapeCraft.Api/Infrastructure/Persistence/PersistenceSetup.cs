using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TapeCraft.Api.Infrastructure.Persistence;

public static class PersistenceSetup
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(provider.GetRequiredService<AppConfig>().ConnectionString));
        services.AddDbContext<TapeCraftDbContext>((provider, options) => Configure(options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<IDatabaseProbe, NpgsqlDatabaseProbe>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention();

    /// <summary>Applies the pending migrations. Returns the process exit code.</summary>
    public static async Task<int> MigrateDatabaseAsync(this IServiceProvider services, TextWriter output, TextWriter errors)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TapeCraftDbContext>().Database;
        try
        {
            var pending = (await database.GetPendingMigrationsAsync()).ToList();
            await database.MigrateAsync();
            await output.WriteLineAsync(pending.Count == 0
                ? "No migrations to run."
                : $"Applied {pending.Count} migration(s): {string.Join(", ", pending)}");
            return 0;
        }
        catch (Exception error)
        {
            await errors.WriteLineAsync($"Migration failed: {error.Message}");
            return 1;
        }
    }
}
