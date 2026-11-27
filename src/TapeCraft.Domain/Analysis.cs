namespace TapeCraft.Domain;

/// <summary>What tape-craft has worked out about a listener's library. Columns follow once the selection basis is decided.</summary>
public sealed class Analysis
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
}
