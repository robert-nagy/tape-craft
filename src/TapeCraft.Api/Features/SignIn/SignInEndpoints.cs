using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Api.Infrastructure.Spotify;

namespace TapeCraft.Api.Features.SignIn;

/// <summary>Sign in with Spotify (authorization code flow with PKCE) and tell the page who is signed in.</summary>
public static class SignInEndpoints
{
    public const string StartPath = "/auth/spotify/sign-in";
    public const string SessionPath = "/api/session";
    public const string Denied = "/?signin=denied";
    public const string Failed = "/?signin=failed";

    public static IEndpointRouteBuilder MapSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(StartPath, Start);
        endpoints.MapGet(SignInConfig.CallbackPath, Callback);
        endpoints.MapGet(SessionPath, CurrentSession);
        return endpoints;
    }

    private static IResult Start(HttpContext context, ISpotify spotify, IDataProtectionProvider protection)
    {
        var attempt = SignInAttempt.Start();
        context.Response.Cookies.Append(SignInAttempt.CookieName, attempt.Protect(protection), SignInAttempt.CookieOptions());
        NoStore(context);
        return Results.Redirect(spotify.AuthorizeUri(attempt.State, attempt.CodeChallenge).ToString());
    }

    private static async Task<IResult> Callback(
        HttpContext context, ISpotify spotify, ISignInStore store, IDataProtectionProvider protection, TimeProvider time,
        ILoggerFactory logs, string? code, string? state, string? error)
    {
        NoStore(context);
        // An attempt is used once, whatever happens next.
        var attempt = SignInAttempt.Unprotect(protection, context.Request.Cookies[SignInAttempt.CookieName]);
        context.Response.Cookies.Delete(SignInAttempt.CookieName, SignInAttempt.CookieOptions());

        // The state is checked first, so that nobody can forge any answer, a denial included.
        var log = logs.CreateLogger(typeof(SignInEndpoints));
        if (attempt is null || !attempt.Matches(state))
        {
            log.LogWarning("Refused a Spotify sign-in: {Reason}", attempt is null ? "no sign-in in progress in this browser" : "the state does not match");
            return Results.Redirect(Failed);
        }
        if (!string.IsNullOrEmpty(error)) return Results.Redirect(Denied);
        if (string.IsNullOrEmpty(code)) return Results.Redirect(Failed);

        try
        {
            var cancellation = context.RequestAborted;
            var grant = await spotify.ExchangeCodeAsync(code, attempt.CodeVerifier, cancellation);
            var profile = await spotify.GetProfileAsync(grant.AccessToken, cancellation);
            var user = await store.SaveSignInAsync(profile, grant, time.GetUtcNow(), cancellation);

            // The session holds who the listener is, never a Spotify token.
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.DisplayName ?? user.SpotifyUserId)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        }
        catch (Exception failure) when (!context.RequestAborted.IsCancellationRequested)
        {
            // The message names the step and status only; it never holds a code or token.
            log.LogWarning("Spotify sign-in failed: {Type}: {Message}", failure.GetType().Name, failure.Message);
            return Results.Redirect(Failed);
        }
    }

    private static IResult CurrentSession(HttpContext context)
    {
        NoStore(context);
        var user = context.User;
        return user.Identity?.IsAuthenticated == true
            ? Results.Json(new { signedIn = true, name = user.FindFirstValue(ClaimTypes.Name) })
            : Results.Json(new { signedIn = false });
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}
