namespace TapeCraft.Domain;

/// <summary>A listener who signs in with their Spotify account. Sign-in (#3) adds its columns.</summary>
public sealed class User
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
}
