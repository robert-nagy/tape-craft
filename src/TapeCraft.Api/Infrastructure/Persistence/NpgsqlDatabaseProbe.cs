using Npgsql;

namespace TapeCraft.Api.Infrastructure.Persistence;

public sealed class NpgsqlDatabaseProbe(NpgsqlDataSource dataSource) : IDatabaseProbe
{
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        command.CommandTimeout = 2;
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
