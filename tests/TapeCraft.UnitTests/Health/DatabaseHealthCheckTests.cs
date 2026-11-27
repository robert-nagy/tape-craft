using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TapeCraft.Api.Features.Health;

namespace TapeCraft.UnitTests.Health;

public sealed class DatabaseHealthCheckTests
{
    private readonly FakeDatabaseProbe _probe = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger _log = new();
    private readonly DatabaseHealthCheck _check;

    public DatabaseHealthCheckTests() => _check = new DatabaseHealthCheck(_probe, _log, _time);

    private Task<HealthCheckResult> Check() => _check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Is_healthy_when_the_database_answers()
    {
        Assert.Equal(HealthStatus.Healthy, (await Check()).Status);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public async Task Is_unhealthy_and_logs_the_cause_when_the_database_fails()
    {
        _probe.Answer = _ => Task.FromException(new InvalidOperationException("Failed to connect", new TimeoutException("no answer")));
        Assert.Equal(HealthStatus.Unhealthy, (await Check()).Status);
        Assert.Equal(["Database check failed: Failed to connect (no answer)"], _log.Messages);
    }

    [Fact]
    public async Task Is_unhealthy_when_the_probe_throws_before_returning_a_task()
    {
        _probe.Answer = _ => throw new ObjectDisposedException("data source");
        Assert.Equal(HealthStatus.Unhealthy, (await Check()).Status);
    }

    [Fact]
    public async Task Gives_up_at_the_deadline_and_cancels_the_probe()
    {
        var cancelled = new TaskCompletionSource();
        _probe.Answer = token =>
        {
            token.Register(cancelled.SetResult);
            return Task.Delay(Timeout.Infinite, CancellationToken.None);
        };

        var result = Check();
        _time.Advance(DatabaseHealthCheck.Deadline - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(HealthStatus.Unhealthy, (await result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Status);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Shares_one_probe_between_concurrent_checks_then_probes_again()
    {
        var answer = new TaskCompletionSource();
        _probe.Answer = _ => answer.Task;

        var both = Task.WhenAll(Check(), Check());
        Assert.Equal(1, _probe.Calls);
        answer.SetResult();
        Assert.All(await both, result => Assert.Equal(HealthStatus.Healthy, result.Status));

        // The finished probe is released in the background; a later check starts a new one.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_probe.Calls < 2 && DateTime.UtcNow < deadline)
        {
            await Check();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, _probe.Calls);
    }

    private sealed class ListLogger : ILogger<DatabaseHealthCheck>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages) Messages.Add(formatter(state, exception));
        }
    }
}
