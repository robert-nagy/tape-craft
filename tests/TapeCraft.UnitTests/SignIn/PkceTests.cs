using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using TapeCraft.Api;
using TapeCraft.Api.Features.SignIn;
using TapeCraft.Api.Infrastructure.Spotify;

namespace TapeCraft.UnitTests.SignIn;

public sealed class PkceTests
{
    [Fact]
    public void Makes_a_url_safe_verifier_and_its_s256_challenge()
    {
        var attempt = SignInAttempt.Start();
        Assert.InRange(attempt.CodeVerifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", attempt.CodeVerifier);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", attempt.State);
        var expected = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.CodeVerifier)));
        Assert.Equal(expected, attempt.CodeChallenge);
    }

    [Fact]
    public void Makes_a_new_state_and_verifier_each_time()
    {
        var attempts = Enumerable.Range(0, 20).Select(_ => SignInAttempt.Start()).ToList();
        Assert.Equal(20, attempts.Select(a => a.State).Distinct().Count());
        Assert.Equal(20, attempts.Select(a => a.CodeVerifier).Distinct().Count());
    }

    [Fact]
    public void Compares_state_values_exactly()
    {
        var attempt = SignInAttempt.Start();
        Assert.True(attempt.Matches(attempt.State));
        Assert.False(attempt.Matches(attempt.State.ToUpperInvariant() == attempt.State ? attempt.State + "x" : attempt.State.ToUpperInvariant()));
        Assert.False(attempt.Matches(attempt.State[..^1]));
        Assert.False(attempt.Matches(null));
        Assert.False(attempt.Matches(""));
    }

    [Fact]
    public void Asks_spotify_for_exactly_the_four_scopes_with_pkce_and_state()
    {
        var config = new SignInConfig("client-id", new Uri("http://127.0.0.1:3000/auth/spotify/callback"), "/keys");
        var url = SpotifyClient.AuthorizeUri(config, "the-state", "the-challenge");

        Assert.Equal("https://accounts.spotify.com/authorize", url.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["client_id"] = "client-id",
                ["response_type"] = "code",
                ["redirect_uri"] = "http://127.0.0.1:3000/auth/spotify/callback",
                ["scope"] = "user-library-read playlist-modify-private playlist-modify-public playlist-read-private",
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = "the-challenge",
                ["state"] = "the-state",
            },
            query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
    }
}
