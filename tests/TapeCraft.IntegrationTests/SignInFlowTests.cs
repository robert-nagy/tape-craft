using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TapeCraft.Api;
using TapeCraft.Api.Infrastructure.Spotify;
using TapeCraft.Domain;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

/// <summary>The whole sign-in through the real app and PostgreSQL; only Spotify is replaced.</summary>
public sealed class SignInFlowTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = Postgres.Create();

    private sealed class Spotify(SignInConfig config) : ISpotify
    {
        public int Exchanges;

        public Uri AuthorizeUri(string state, string codeChallenge) => SpotifyClient.AuthorizeUri(config, state, codeChallenge);

        public Task<SpotifyGrant> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Exchanges);
            return Task.FromResult(new SpotifyGrant("BQD-flow-access", "AQD-flow-refresh", 3600));
        }

        public Task<SpotifyProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new SpotifyProfile("flow-spotify-id", "Flo"));
    }

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await using var migrate = AppProcess.Start(Postgres.DatabaseUrl(_database), args: "migrate");
        var code = await migrate.WaitForExitAsync(TimeSpan.FromSeconds(60));
        Assert.True(code == 0, migrate.Output);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    private async Task<List<string>> Raw(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return rows;
    }

    [Fact]
    public async Task Signs_a_listener_in_and_stores_only_encrypted_tokens()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var factory = new AppFactory(Postgres.DatabaseUrl(_database));
        var spotify = (Spotify?)null;
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ISpotify>(provider => spotify = new Spotify(provider.GetRequiredService<SignInConfig>()))));
        using var browser = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        for (var signIn = 0; signIn < 2; signIn++)
        {
            var start = await browser.GetAsync("/auth/spotify/sign-in", cancellation);
            var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"].ToString();
            var callback = await browser.GetAsync($"/auth/spotify/callback?code=the-code&state={state}", cancellation);
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal("/", callback.Headers.Location?.OriginalString);
        }

        Assert.Equal(2, spotify!.Exchanges);
        Assert.Contains("\"name\":\"Flo\"", await browser.GetStringAsync("/api/session", cancellation));
        Assert.Equal(["flow-spotify-id"], await Raw("SELECT spotify_user_id FROM users"));
        var stored = Assert.Single(await Raw("SELECT access_token || ' ' || refresh_token FROM tokens"));
        Assert.DoesNotContain("flow-access", stored);
        Assert.DoesNotContain("flow-refresh", stored);
    }
}
