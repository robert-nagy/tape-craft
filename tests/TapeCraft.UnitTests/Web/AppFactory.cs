using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TapeCraft.Api.Infrastructure.Persistence;

namespace TapeCraft.UnitTests.Web;

/// <summary>The real app in memory, with the database replaced by <see cref="Probe"/>.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public FakeDatabaseProbe Probe { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so that wwwroot is served as built, as in production.
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", "postgres://127.0.0.1:1/tapecraft");
        builder.ConfigureTestServices(services => services.AddSingleton<IDatabaseProbe>(Probe));
    }
}
