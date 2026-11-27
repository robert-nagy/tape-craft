using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace TapeCraft.IntegrationTests;

/// <summary>Needs no Docker: the database is a closed port or a server that never answers.</summary>
public sealed class UnreachableDatabaseTests : IAsyncLifetime
{
    // Accepts connections and then says nothing.
    private readonly TcpListener _blackHole = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _held = [];
    private CancellationTokenSource _stop = new();

    private int BlackHolePort => ((IPEndPoint)_blackHole.LocalEndpoint).Port;

    public ValueTask InitializeAsync()
    {
        _blackHole.Start();
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _blackHole.AcceptTcpClientAsync(_stop.Token);
                lock (_held) _held.Add(client);
            }
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _blackHole.Stop();
        lock (_held) _held.ForEach(client => client.Dispose());
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Url(string kind) => kind == "refuses connections"
        ? $"postgres://127.0.0.1:{Postgres.FreePort()}/tapecraft"
        : $"postgres://127.0.0.1:{BlackHolePort}/tapecraft";

    [Theory]
    [InlineData("refuses connections")]
    [InlineData("never answers")]
    public async Task Health_is_503_within_3_seconds_when_the_database(string kind)
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var app = new AppFactory(Url(kind));
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var timer = Stopwatch.StartNew();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/healthz", cancellation)).StatusCode);
            Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task Stopping_lets_a_request_in_progress_finish()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var app = await AppProcess.StartServerAsync(Url("never answers"), cancellation);
        using var client = new HttpClient();
        var response = client.GetAsync(app.Url + "/healthz", cancellation);
        await WaitUntil(() => { lock (_held) return _held.Count > 0; });

        app.Terminate();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await response).StatusCode);
        Assert.Equal(0, await app.WaitForExitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Migrate_fails_without_printing_the_password()
    {
        await using var app = AppProcess.Start($"postgres://tape:hunter2@127.0.0.1:{Postgres.FreePort()}/tapecraft", args: "migrate");
        Assert.Equal(1, await app.WaitForExitAsync(TimeSpan.FromSeconds(60)));
        Assert.Contains("Migration failed", app.Output);
        Assert.DoesNotContain("hunter2", app.Output);
    }

    [Fact]
    public async Task A_bad_setting_stops_the_app_with_a_message()
    {
        await using var app = AppProcess.Start("mysql://tape:hunter2@127.0.0.1/tapecraft");
        Assert.Equal(1, await app.WaitForExitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains("DATABASE_URL must start with postgres", app.Output);
        Assert.DoesNotContain("hunter2", app.Output);
    }

    [Theory]
    [InlineData("SPOTIFY_CLIENT_ID", "SPOTIFY_CLIENT_ID is required")]
    [InlineData("SPOTIFY_REDIRECT_URI", "SPOTIFY_REDIRECT_URI is required")]
    [InlineData("DATA_PROTECTION_KEYS_DIR", "DATA_PROTECTION_KEYS_DIR is required")]
    public async Task The_web_server_does_not_start_without_a_sign_in_setting(string name, string message)
    {
        await using var app = AppProcess.Start("postgres://127.0.0.1:1/tapecraft", 0, new Dictionary<string, string?> { [name] = null });
        Assert.Equal(1, await app.WaitForExitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains(message, app.Output);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
