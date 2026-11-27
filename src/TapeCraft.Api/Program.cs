using TapeCraft.Api;
using Microsoft.AspNetCore.DataProtection;
using TapeCraft.Api.Features.Health;
using TapeCraft.Api.Features.SignIn;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Api.Infrastructure.Spotify;
using TapeCraft.Api.Web;

var builder = WebApplication.CreateBuilder(args);
var migrating = args is ["migrate"];

// EF Core logs a failed query while it looks for its history table in a new database.
// A real failure still ends the command with "Migration failed".
if (migrating) builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);

// Read when first needed, so that tests can supply the settings.
builder.Services.AddSingleton(provider => AppConfig.Load(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(provider => SignInConfig.Load(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddPersistence();
// Migrations store no tokens, so they need neither the key ring nor its settings.
if (migrating) builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
else builder.Services.AddDataProtectionKeys();
builder.Services.AddSessionCookie();
builder.Services.AddSpotify();
builder.Services.AddHealthFeature();
// Requests in progress get this long to finish when the app is asked to stop.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

var app = builder.Build();

AppConfig config;
try
{
    config = app.Services.GetRequiredService<AppConfig>();
    if (!migrating) app.Services.GetRequiredService<SignInConfig>();
}
catch (AppConfigException error)
{
    await Console.Error.WriteLineAsync(error.Message);
    return 1;
}

if (migrating) return await app.Services.MigrateDatabaseAsync(Console.Out, Console.Error);

// Load or create the key ring now, so that an unusable DATA_PROTECTION_KEYS_DIR stops the app instead of every sign-in.
try
{
    app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("TapeCraft.StartupCheck").Protect("ok");
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync($"DATA_PROTECTION_KEYS_DIR cannot hold the key ring: {error.Message}");
    return 1;
}

app.Urls.Add($"http://{(config.Host.Contains(':') ? $"[{config.Host}]" : config.Host)}:{config.Port}");
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoint();
app.MapSignInEndpoints();
await app.RunAsync();
return 0;
