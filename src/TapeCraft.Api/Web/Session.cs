using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace TapeCraft.Api.Web;

/// <summary>The Data Protection key ring and the listener's session cookie.</summary>
public static class Session
{
    public const string CookieName = "tapecraft.session";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// Keeps the key ring in DATA_PROTECTION_KEYS_DIR, outside the container (ADR 0001), so that a redeploy
    /// neither signs listeners out nor makes their stored tokens unreadable.
    /// </summary>
    public static IServiceCollection AddDataProtectionKeys(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("tape-craft");
        services.AddOptions<KeyManagementOptions>().Configure<SignInConfig, ILoggerFactory>((options, config, logs) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(config.DataProtectionKeysDirectory), logs));
        return services;
    }

    public static IServiceCollection AddSessionCookie(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = Lifetime;
            options.SlidingExpiration = true;
            // An API caller without a session gets a status code, not a redirect to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization();
        return services;
    }
}
