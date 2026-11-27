namespace TapeCraft.Domain;

/// <summary>The tokens Spotify grants when a listener signs in.</summary>
public sealed record SpotifyGrant(string AccessToken, string RefreshToken, int ExpiresInSeconds)
{
    // Never print the tokens, for example in a log line or a failed assertion.
    public override string ToString() => $"SpotifyGrant {{ ExpiresInSeconds = {ExpiresInSeconds} }}";
}

/// <summary>Who the listener is on Spotify.</summary>
public sealed record SpotifyProfile(string Id, string? DisplayName);
