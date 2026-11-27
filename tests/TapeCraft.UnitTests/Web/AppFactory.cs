using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TapeCraft.Api;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Api.Infrastructure.Spotify;
using TapeCraft.UnitTests.SignIn;

namespace TapeCraft.UnitTests.Web;

/// <summary>
/// The real app in memory, with the database replaced by <see cref="Probe"/> and <see cref="Store"/>,
/// and Spotify by <see cref="Spotify"/>.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _keys = Directory.CreateTempSubdirectory("tape-craft-keys-").FullName;

    public FakeDatabaseProbe Probe { get; } = new();

    public FakeSignInStore Store { get; } = new();

    public FakeSpotify Spotify => Services.GetRequiredService<FakeSpotify>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so that wwwroot is served as built, as in production.
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", "postgres://127.0.0.1:1/tapecraft");
        builder.UseSetting("SPOTIFY_CLIENT_ID", "testclientid0123");
        builder.UseSetting("SPOTIFY_REDIRECT_URI", "https://localhost/auth/spotify/callback");
        builder.UseSetting("DATA_PROTECTION_KEYS_DIR", _keys);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDatabaseProbe>(Probe);
            services.AddSingleton<ISignInStore>(Store);
            services.AddSingleton(provider => new FakeSpotify(provider.GetRequiredService<SignInConfig>()));
            services.AddSingleton<ISpotify>(provider => provider.GetRequiredService<FakeSpotify>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_keys, recursive: true);
    }
}
