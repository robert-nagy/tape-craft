using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TapeCraft.Api.Infrastructure.Persistence;

/// <summary>
/// Healthy when the database answers within the deadline, which keeps <c>/healthz</c>
/// under 3 seconds even when a connection hangs. Concurrent checks share one probe,
/// so a public endpoint cannot use up the connection pool.
/// </summary>
public sealed class DatabaseHealthCheck(IDatabaseProbe probe, ILogger<DatabaseHealthCheck> logger, TimeProvider time)
    : IHealthCheck
{
    public static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(2500);

    private readonly Lock _gate = new();
    private Task<bool>? _pending;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Task<bool> check;
        lock (_gate)
        {
            if (_pending is null)
            {
                var started = ProbeWithDeadlineAsync();
                _pending = started;
                started.ContinueWith(_ => Clear(started), TaskScheduler.Default);
            }
            check = _pending;
        }

        return await check ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }

    private void Clear(Task<bool> finished)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pending, finished)) _pending = null;
        }
    }

    private async Task<bool> ProbeWithDeadlineAsync()
    {
        using var cancellation = new CancellationTokenSource(Deadline, time);
        Task? running = null;
        try
        {
            running = probe.ProbeAsync(cancellation.Token);
            await running.WaitAsync(Deadline, time);
            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning("Database check failed: {Cause}", Describe(error));
            return false;
        }
        finally
        {
            await cancellation.CancelAsync();
            // A probe abandoned at the deadline may still fail later; observe it so it is not reported as unobserved.
            running?.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static string Describe(Exception error) =>
        error.InnerException is { } inner ? $"{error.Message} ({inner.Message})" : error.Message;
}
