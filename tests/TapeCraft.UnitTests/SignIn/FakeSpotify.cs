using System.Collections.Concurrent;
using TapeCraft.Api;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Api.Infrastructure.Spotify;
using TapeCraft.Domain;

namespace TapeCraft.UnitTests.SignIn;

/// <summary>Spotify as tests need it: records every call and answers with <see cref="Grant"/> and <see cref="Profile"/>.</summary>
public sealed class FakeSpotify(SignInConfig config) : ISpotify
{
    public ConcurrentQueue<(string Code, string Verifier)> Exchanges { get; } = new();

    public SpotifyGrant Grant { get; set; } = new("BQD-access-token", "AQD-refresh-token", 3600);

    public SpotifyProfile Profile { get; set; } = new("ada-spotify-id", "Ada");

    public Exception? ExchangeFailure { get; set; }

    public Uri AuthorizeUri(string state, string codeChallenge) => SpotifyClient.AuthorizeUri(config, state, codeChallenge);

    public Task<SpotifyGrant> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        Exchanges.Enqueue((code, codeVerifier));
        return ExchangeFailure is null ? Task.FromResult(Grant) : Task.FromException<SpotifyGrant>(ExchangeFailure);
    }

    public Task<SpotifyProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken) =>
        accessToken == Grant.AccessToken ? Task.FromResult(Profile) : throw new InvalidOperationException("unexpected access token");
}

public sealed class FakeSignInStore : ISignInStore
{
    public ConcurrentQueue<(SpotifyProfile Profile, SpotifyGrant Grant)> Saved { get; } = new();

    public Task<User> SaveSignInAsync(SpotifyProfile profile, SpotifyGrant grant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Saved.Enqueue((profile, grant));
        return Task.FromResult(User.Register(profile, now));
    }
}
