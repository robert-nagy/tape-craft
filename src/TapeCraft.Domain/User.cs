namespace TapeCraft.Domain;

/// <summary>A listener who signs in with their Spotify account.</summary>
public sealed class User
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>The listener's Spotify user ID; one user per Spotify account.</summary>
    public string SpotifyUserId { get; private set; } = "";

    public string? DisplayName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static User Register(SpotifyProfile profile, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id, nameof(profile));
        return new User { SpotifyUserId = profile.Id, DisplayName = profile.DisplayName, CreatedAt = now };
    }

    /// <summary>Takes the profile Spotify returned at a later sign-in.</summary>
    public void UpdateProfile(SpotifyProfile profile)
    {
        if (profile.Id != SpotifyUserId) throw new ArgumentException("The profile belongs to another Spotify user", nameof(profile));
        DisplayName = profile.DisplayName;
    }
}
