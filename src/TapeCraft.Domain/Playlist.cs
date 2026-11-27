namespace TapeCraft.Domain;

/// <summary>A playlist tape-craft crafted and saved to Spotify. Saving (#7) adds its columns.</summary>
public sealed class Playlist
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
}
