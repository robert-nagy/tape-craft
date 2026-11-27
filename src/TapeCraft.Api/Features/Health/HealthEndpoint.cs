using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TapeCraft.Api.Features.Health;

/// <summary><c>GET /healthz</c>: 200 when the database answers, 503 when it does not.</summary>
public static class HealthEndpoint
{
    public static IServiceCollection AddHealthFeature(this IServiceCollection services)
    {
        services.AddSingleton<DatabaseHealthCheck>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            // The body names no cause; DatabaseHealthCheck logs it.
            ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(
                new { status = report.Status == HealthStatus.Healthy ? "ok" : "unavailable" }),
        });
        return endpoints;
    }
}
