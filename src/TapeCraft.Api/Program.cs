using TapeCraft.Api;
using TapeCraft.Api.Features.Health;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Api.Web;

var builder = WebApplication.CreateBuilder(args);
var migrating = args is ["migrate"];

// EF Core logs a failed query while it looks for its history table in a new database.
// A real failure still ends the command with "Migration failed".
if (migrating) builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);

// Read when first needed, so that tests can supply the settings.
builder.Services.AddSingleton(provider => AppConfig.Load(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddPersistence();
builder.Services.AddHealthFeature();
// Requests in progress get this long to finish when the app is asked to stop.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

var app = builder.Build();

AppConfig config;
try
{
    config = app.Services.GetRequiredService<AppConfig>();
}
catch (AppConfigException error)
{
    await Console.Error.WriteLineAsync(error.Message);
    return 1;
}

if (migrating) return await app.Services.MigrateDatabaseAsync(Console.Out, Console.Error);

app.Urls.Add($"http://{(config.Host.Contains(':') ? $"[{config.Host}]" : config.Host)}:{config.Port}");
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthEndpoint();
await app.RunAsync();
return 0;
