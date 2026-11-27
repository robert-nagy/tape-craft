namespace TapeCraft.Domain;

/// <summary>A track from a listener's Liked Songs. Library sync (#4) adds its columns.</summary>
public sealed class Track
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
}
