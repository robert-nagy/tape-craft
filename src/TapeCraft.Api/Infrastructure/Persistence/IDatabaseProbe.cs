namespace TapeCraft.Api.Infrastructure.Persistence;

/// <summary>Asks the database for a trivial answer; throws when it cannot.</summary>
public interface IDatabaseProbe
{
    Task ProbeAsync(CancellationToken cancellationToken);
}
