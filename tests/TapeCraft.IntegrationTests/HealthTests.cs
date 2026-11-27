using System.Diagnostics;
using System.Net;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

public sealed class HealthTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = Postgres.Create();

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Is_200_while_the_database_runs_then_503_within_3_seconds_once_it_stops()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var app = new AppFactory(Postgres.DatabaseUrl(_database));
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz", cancellation)).StatusCode);

        await _database.StopAsync(cancellation);
        var timer = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/healthz", cancellation)).StatusCode);
        Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", cancellation)).StatusCode);
    }

    [Fact]
    public async Task The_built_app_serves_the_landing_page_and_reports_healthy()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var app = await AppProcess.StartServerAsync(Postgres.DatabaseUrl(_database), cancellation);
        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(app.Url + "/", cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(app.Url + "/healthz", cancellation)).StatusCode);
    }
}
