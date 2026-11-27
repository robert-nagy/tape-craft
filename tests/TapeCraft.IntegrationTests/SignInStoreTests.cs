using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Domain;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

public sealed class SignInStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly SpotifyProfile Ada = new("ada-spotify-id", "Ada");
    private static readonly SpotifyGrant Grant = new("BQD-access-token-secret", "AQD-refresh-token-secret", 3600);

    private readonly PostgreSqlContainer _database = Postgres.Create();
    private readonly string _keys = Directory.CreateTempSubdirectory("tape-craft-keys-").FullName;

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await using var app = AppProcess.Start(Postgres.DatabaseUrl(_database), args: "migrate");
        var code = await app.WaitForExitAsync(TimeSpan.FromSeconds(60));
        Assert.True(code == 0, app.Output);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_keys, recursive: true);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<T> InApp<T>(Func<IServiceProvider, Task<T>> use, string? keys = null)
    {
        await using var app = new AppFactory(Postgres.DatabaseUrl(_database), keys ?? _keys);
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider);
    }

    private Task<User> SignIn(SpotifyProfile profile, SpotifyGrant grant, DateTimeOffset now, string? keys = null) =>
        InApp(services => services.GetRequiredService<ISignInStore>().SaveSignInAsync(profile, grant, now, Cancel), keys);

    private Task<Token> ReadToken(Guid userId, string? keys = null) =>
        InApp(services => services.GetRequiredService<TapeCraftDbContext>().Tokens.AsNoTracking().SingleAsync(t => t.UserId == userId, Cancel), keys);

    private async Task<List<string>> Raw(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(Cancel);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        var rows = new List<string>();
        while (await reader.ReadAsync(Cancel))
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i)))));
        return rows;
    }

    [Fact]
    public async Task AC6_stores_both_tokens_encrypted()
    {
        var user = await SignIn(Ada, Grant, Now);

        var stored = Assert.Single(await Raw("SELECT access_token || ' ' || refresh_token FROM tokens"));
        Assert.DoesNotContain("BQD-access-token-secret", stored);
        Assert.DoesNotContain("AQD-refresh-token-secret", stored);
        Assert.DoesNotContain("token-secret", stored);

        var token = await ReadToken(user.Id);
        Assert.Equal(("BQD-access-token-secret", "AQD-refresh-token-secret", Now.AddHours(1)), (token.AccessToken, token.RefreshToken, token.ExpiresAt));
    }

    [Fact]
    public async Task Another_app_instance_with_the_same_key_directory_reads_the_tokens()
    {
        var user = await SignIn(Ada, Grant, Now);
        Assert.NotEmpty(Directory.GetFiles(_keys, "key-*.xml"));
        // A new app has its own Data Protection provider; only the key ring on disk is shared.
        var token = await ReadToken(user.Id);
        Assert.Equal("AQD-refresh-token-secret", token.RefreshToken);
    }

    [Fact]
    public async Task An_app_with_another_key_ring_cannot_read_the_tokens()
    {
        var user = await SignIn(Ada, Grant, Now);
        var otherKeys = Directory.CreateTempSubdirectory("tape-craft-keys-").FullName;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => ReadToken(user.Id, otherKeys));
        }
        finally
        {
            Directory.Delete(otherKeys, recursive: true);
        }
    }

    [Fact]
    public async Task A_second_sign_in_updates_the_same_listener_and_replaces_the_tokens()
    {
        var first = await SignIn(Ada, Grant, Now);
        var second = await SignIn(Ada with { DisplayName = "Ada L." }, new SpotifyGrant("BQD-2", "AQD-2", 60), Now.AddDays(1));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(["ada-spotify-id|Ada L."], await Raw("SELECT spotify_user_id, display_name FROM users"));
        Assert.Single(await Raw("SELECT id FROM tokens"));
        var token = await ReadToken(first.Id);
        Assert.Equal(("BQD-2", "AQD-2", Now.AddDays(1).AddMinutes(1)), (token.AccessToken, token.RefreshToken, token.ExpiresAt));
    }

    [Fact]
    public async Task Keeps_each_listener_apart()
    {
        var ada = await SignIn(Ada, Grant, Now);
        var bob = await SignIn(new SpotifyProfile("bob-spotify-id", null), new SpotifyGrant("BQD-bob", "AQD-bob", 3600), Now);

        Assert.NotEqual(ada.Id, bob.Id);
        Assert.Equal("BQD-bob", (await ReadToken(bob.Id)).AccessToken);
        Assert.Equal("BQD-access-token-secret", (await ReadToken(ada.Id)).AccessToken);
    }
}
