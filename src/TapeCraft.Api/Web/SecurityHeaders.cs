namespace TapeCraft.Api.Web;

public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";

    /// <summary>Adds the security headers to every response. Register it first.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            return next(context);
        });
}
