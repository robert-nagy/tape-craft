using Microsoft.Extensions.Configuration;
using TapeCraft.Api;

namespace TapeCraft.UnitTests.Configuration;

public sealed class SignInConfigTests
{
    private const string ClientId = "0123456789abcdef0123456789abcdef";
    private const string RedirectUri = "http://127.0.0.1:3000/auth/spotify/callback";
    private const string KeysDir = "/var/lib/tape-craft/keys";

    private static SignInConfig Load(string? clientId = ClientId, string? redirectUri = RedirectUri, string? keysDir = KeysDir) =>
        SignInConfig.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SPOTIFY_CLIENT_ID"] = clientId,
                ["SPOTIFY_REDIRECT_URI"] = redirectUri,
                ["DATA_PROTECTION_KEYS_DIR"] = keysDir,
            })
            .Build());

    [Fact]
    public void Reads_the_spotify_app_and_the_key_directory()
    {
        var config = Load(clientId: $" {ClientId} ");
        Assert.Equal((ClientId, new Uri(RedirectUri), KeysDir), (config.ClientId, config.RedirectUri, config.DataProtectionKeysDirectory));
    }

    [Theory]
    [InlineData(null, "SPOTIFY_CLIENT_ID is required")]
    [InlineData("", "SPOTIFY_CLIENT_ID is required")]
    [InlineData("not an id!", "SPOTIFY_CLIENT_ID must contain only letters and digits")]
    public void Explains_a_bad_client_id(string? value, string message) =>
        Assert.Equal(message, Assert.Throws<AppConfigException>(() => Load(clientId: value)).Message);

    [Theory]
    [InlineData(null, "SPOTIFY_REDIRECT_URI is required")]
    [InlineData("/auth/spotify/callback", "SPOTIFY_REDIRECT_URI must be an absolute http or https URL")]
    [InlineData("ftp://example.com/auth/spotify/callback", "SPOTIFY_REDIRECT_URI must be an absolute http or https URL")]
    [InlineData("http://example.com/auth/spotify/callback", "SPOTIFY_REDIRECT_URI must use https unless it points to 127.0.0.1 or [::1]")]
    [InlineData("http://localhost:3000/auth/spotify/callback", "SPOTIFY_REDIRECT_URI must use https unless it points to 127.0.0.1 or [::1]")]
    [InlineData("https://example.com/somewhere-else", "SPOTIFY_REDIRECT_URI must end with /auth/spotify/callback")]
    [InlineData("https://example.com/auth/spotify/callback?x=1", "SPOTIFY_REDIRECT_URI must not have a query or fragment")]
    public void Explains_a_bad_redirect_uri(string? value, string message) =>
        Assert.Equal(message, Assert.Throws<AppConfigException>(() => Load(redirectUri: value)).Message);

    [Theory]
    [InlineData("https://tape-craft.example/auth/spotify/callback")]
    [InlineData("http://127.0.0.1:3000/auth/spotify/callback")]
    [InlineData("http://[::1]:3000/auth/spotify/callback")]
    public void Accepts_https_and_loopback_redirect_uris(string value) => Assert.Equal(new Uri(value), Load(redirectUri: value).RedirectUri);

    [Theory]
    [InlineData(null, "DATA_PROTECTION_KEYS_DIR is required")]
    [InlineData("keys", "DATA_PROTECTION_KEYS_DIR must be an absolute path")]
    public void Explains_a_bad_key_directory(string? value, string message) =>
        Assert.Equal(message, Assert.Throws<AppConfigException>(() => Load(keysDir: value)).Message);
}
