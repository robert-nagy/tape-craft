using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

public sealed class MigrationTests : IAsyncLifetime
{
    private static readonly string[] Tables = ["analysis", "playlists", "tokens", "tracks", "users"];
    private readonly PostgreSqlContainer _database = Postgres.Create();

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    private async Task<List<string>> Query(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i)))));
        return rows;
    }

    private Task<List<string>> PublicTables() =>
        Query("SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT LIKE '\\_\\_%' ORDER BY tablename");

    private async Task<(int Code, string Output)> Migrate()
    {
        await using var app = AppProcess.Start(Postgres.DatabaseUrl(_database), args: "migrate");
        var code = await app.WaitForExitAsync(TimeSpan.FromSeconds(60));
        return (code, app.Output);
    }

    [Fact]
    public async Task Create_the_tables_once_and_change_nothing_when_run_again()
    {
        Assert.Empty(await PublicTables());

        var first = await Migrate();
        Assert.True(first.Code == 0, first.Output);
        Assert.Contains("Applied 1 migration(s)", first.Output);
        Assert.DoesNotContain("fail", first.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warn", first.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Tables, await PublicTables());
        foreach (var table in Tables)
            Assert.Equal(["id|uuid"], await Query($"SELECT column_name, data_type FROM information_schema.columns WHERE table_name = '{table}'"));

        var snapshot = await Query("SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1, 2");
        var history = await Query("SELECT * FROM \"__EFMigrationsHistory\"");

        var second = await Migrate();
        Assert.True(second.Code == 0, second.Output);
        Assert.Contains("No migrations to run.", second.Output);
        Assert.DoesNotContain("fail", second.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(snapshot, await Query("SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1, 2"));
        Assert.Equal(history, await Query("SELECT * FROM \"__EFMigrationsHistory\""));
    }
}
