namespace TapeCraft.Domain;

/// <summary>A listener's Spotify tokens. Sign-in (#3) adds its columns.</summary>
public sealed class Token
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
}
