using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TapeCraft.UnitTests.Web;

namespace TapeCraft.UnitTests.SignIn;

public sealed class SignInHttpTests : IAsyncLifetime
{
    private readonly AppFactory _app = new();
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        // HTTPS, so that the client sends the Secure cookies back, as a browser does.
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<(Dictionary<string, string> Query, SetCookieHeaderValue Cookie)> StartSignIn()
    {
        var response = await _client.GetAsync("/auth/spotify/sign-in", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://accounts.spotify.com/authorize", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        return (query, Assert.Single(Cookies(response), cookie => cookie.Name == "tapecraft.signin"));
    }

    private static IList<SetCookieHeaderValue> Cookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values) ? SetCookieHeaderValue.ParseList(values.ToList()) : [];

    private Task<HttpResponseMessage> Callback(string query) => _client.GetAsync("/auth/spotify/callback?" + query, Cancel);

    private async Task<SessionAnswer> Session()
    {
        var response = await _client.GetAsync("/api/session", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<SessionAnswer>(Cancel))!;
    }

    private sealed record SessionAnswer(bool SignedIn, string? Name);

    private static void AssertSecureCookie(SetCookieHeaderValue cookie)
    {
        Assert.True(cookie.HttpOnly, $"{cookie.Name} is not HttpOnly");
        Assert.True(cookie.Secure, $"{cookie.Name} is not Secure");
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
    }

    [Fact]
    public async Task AC1_sends_the_listener_to_spotify_with_the_four_scopes_pkce_and_a_random_state()
    {
        var (query, cookie) = await StartSignIn();
        Assert.Equal("user-library-read playlist-modify-private playlist-modify-public playlist-read-private", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", query["code_challenge"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", query["state"]);
        Assert.Equal(("testclientid0123", "code"), (query["client_id"], query["response_type"]));
        Assert.Equal("https://localhost/auth/spotify/callback", query["redirect_uri"]);
        AssertSecureCookie(cookie);
        Assert.Equal("/auth/spotify", cookie.Path.ToString());
        Assert.DoesNotContain(query["state"], cookie.Value.ToString());

        var (second, _) = await StartSignIn();
        Assert.NotEqual(query["state"], second["state"]);
        Assert.NotEqual(query["code_challenge"], second["code_challenge"]);
    }

    [Fact]
    public async Task AC2_exchanges_the_code_with_the_stored_verifier_and_signs_the_listener_in()
    {
        var (query, _) = await StartSignIn();
        var response = await Callback($"code=the-code&state={query["state"]}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var (code, verifier) = Assert.Single(_app.Spotify.Exchanges);
        Assert.Equal("the-code", code);
        Assert.Equal(query["code_challenge"], WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        var saved = Assert.Single(_app.Store.Saved);
        Assert.Equal(("ada-spotify-id", "BQD-access-token"), (saved.Profile.Id, saved.Grant.AccessToken));
        Assert.Equal(new SessionAnswer(true, "Ada"), await Session());
    }

    [Fact]
    public async Task AC2_uses_a_sign_in_attempt_only_once()
    {
        var (query, _) = await StartSignIn();
        await Callback($"code=the-code&state={query["state"]}");
        var replay = await Callback($"code=the-code&state={query["state"]}");
        Assert.Equal("/?signin=failed", replay.Headers.Location?.OriginalString);
        Assert.Single(_app.Spotify.Exchanges);
    }

    [Theory]
    [InlineData("code=the-code&state=wrong-state")]
    [InlineData("code=the-code")]
    [InlineData("code=the-code&state=")]
    public async Task AC3_refuses_a_missing_or_different_state_without_asking_spotify(string callback)
    {
        await StartSignIn();
        var response = await Callback(callback);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?signin=failed", response.Headers.Location?.OriginalString);
        Assert.Empty(_app.Spotify.Exchanges);
        Assert.Empty(_app.Store.Saved);
        Assert.DoesNotContain(Cookies(response), cookie => cookie.Name == "tapecraft.session");
        Assert.Equal(new SessionAnswer(false, null), await Session());
    }

    [Fact]
    public async Task AC3_refuses_a_callback_from_a_browser_that_never_started_signing_in()
    {
        var (query, _) = await StartSignIn();
        using var stranger = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await stranger.GetAsync($"/auth/spotify/callback?code=the-code&state={query["state"]}", Cancel);
        Assert.Equal("/?signin=failed", response.Headers.Location?.OriginalString);
        Assert.Empty(_app.Spotify.Exchanges);
    }

    [Fact]
    public async Task AC3_refuses_a_tampered_sign_in_cookie()
    {
        var (query, cookie) = await StartSignIn();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/auth/spotify/callback?code=the-code&state={query["state"]}");
        request.Headers.Add("Cookie", $"tapecraft.signin={cookie.Value}x");
        using var forger = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        var response = await forger.SendAsync(request, Cancel);
        Assert.Equal("/?signin=failed", response.Headers.Location?.OriginalString);
        Assert.Empty(_app.Spotify.Exchanges);
    }

    [Fact]
    public async Task AC4_shows_the_signed_out_page_when_the_listener_denies_access()
    {
        var (query, _) = await StartSignIn();
        var response = await Callback($"error=access_denied&state={query["state"]}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?signin=denied", response.Headers.Location?.OriginalString);
        Assert.Empty(_app.Spotify.Exchanges);
        Assert.Empty(_app.Store.Saved);
        Assert.Equal(new SessionAnswer(false, null), await Session());
    }

    [Fact]
    public async Task AC4_does_not_believe_a_denial_with_a_different_state()
    {
        await StartSignIn();
        var response = await Callback("error=access_denied&state=wrong");
        Assert.Equal("/?signin=failed", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AC5_keeps_the_session_in_a_secure_cookie_without_any_spotify_token()
    {
        var (query, _) = await StartSignIn();
        var response = await Callback($"code=the-code&state={query["state"]}");

        var session = Assert.Single(Cookies(response), cookie => cookie.Name == "tapecraft.session");
        AssertSecureCookie(session);
        Assert.Equal("/", session.Path.ToString());
        var value = session.Value.ToString();
        Assert.DoesNotContain("BQD-access-token", value);
        Assert.DoesNotContain("AQD-refresh-token", value);
        var signIn = Assert.Single(Cookies(response), cookie => cookie.Name == "tapecraft.signin");
        Assert.True(signIn.Expires < DateTimeOffset.UtcNow, "the sign-in attempt cookie is not deleted");
    }

    [Fact]
    public async Task Shows_an_error_when_spotify_refuses_the_code()
    {
        _app.Spotify.ExchangeFailure = new HttpRequestException("invalid_grant");
        var (query, _) = await StartSignIn();
        var response = await Callback($"code=the-code&state={query["state"]}");
        Assert.Equal("/?signin=failed", response.Headers.Location?.OriginalString);
        Assert.Empty(_app.Store.Saved);
        Assert.Equal(new SessionAnswer(false, null), await Session());
    }

    [Fact]
    public async Task Says_signed_out_to_a_new_browser() => Assert.Equal(new SessionAnswer(false, null), await Session());
}
