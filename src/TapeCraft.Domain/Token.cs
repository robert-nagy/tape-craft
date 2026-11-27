namespace TapeCraft.Domain;

/// <summary>
/// A listener's Spotify tokens, one row per user. The database stores both tokens encrypted;
/// this class holds them in clear text only in memory.
/// </summary>
public sealed class Token
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid UserId { get; private set; }

    public string AccessToken { get; private set; } = "";

    public string RefreshToken { get; private set; } = "";

    public DateTimeOffset ExpiresAt { get; private set; }

    public static Token Issue(Guid userId, SpotifyGrant grant, DateTimeOffset now)
    {
        var token = new Token { UserId = userId };
        token.Replace(grant, now);
        return token;
    }

    /// <summary>Takes the tokens Spotify granted at a later sign-in.</summary>
    public void Replace(SpotifyGrant grant, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(grant.AccessToken, nameof(grant));
        ArgumentException.ThrowIfNullOrEmpty(grant.RefreshToken, nameof(grant));
        if (grant.ExpiresInSeconds <= 0) throw new ArgumentException("The grant has already expired", nameof(grant));
        AccessToken = grant.AccessToken;
        RefreshToken = grant.RefreshToken;
        ExpiresAt = now.AddSeconds(grant.ExpiresInSeconds);
    }
}
