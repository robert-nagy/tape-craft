using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Spotify;

/// <summary>
/// The calls tape-craft makes to Spotify: the Accounts service for sign-in and the Web API for the profile.
/// Methods throw when Spotify refuses or cannot be reached.
/// </summary>
public interface ISpotify
{
    /// <summary>Spotify's authorization page for this app, with PKCE (S256) and <paramref name="state"/>.</summary>
    Uri AuthorizeUri(string state, string codeChallenge);

    Task<SpotifyGrant> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    Task<SpotifyProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken);
}
