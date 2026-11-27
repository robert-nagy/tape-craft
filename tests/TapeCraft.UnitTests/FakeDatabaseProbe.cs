using TapeCraft.Api.Infrastructure.Persistence;

namespace TapeCraft.UnitTests;

public sealed class FakeDatabaseProbe : IDatabaseProbe
{
    private int _calls;

    public Func<CancellationToken, Task> Answer { get; set; } = _ => Task.CompletedTask;

    public int Calls => Volatile.Read(ref _calls);

    public Task ProbeAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Answer(cancellationToken);
    }
}
