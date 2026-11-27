using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using TapeCraft.Api;
using TapeCraft.Api.Infrastructure.Spotify;

namespace TapeCraft.UnitTests.SignIn;

public sealed class SpotifyClientTests
{
    private static readonly SignInConfig Config = new("clientid", new Uri("https://tape-craft.example/auth/spotify/callback"), "/keys");

    private sealed class Recorder(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (SpotifyClient Client, Recorder Recorder) Client(HttpStatusCode status, string body)
    {
        var recorder = new Recorder(status, body);
        return (new SpotifyClient(new HttpClient(recorder), Config), recorder);
    }

    [Fact]
    public async Task Exchanges_the_code_with_the_verifier_and_no_client_secret()
    {
        var (client, recorder) = Client(HttpStatusCode.OK, """{"access_token":"BQD","token_type":"Bearer","scope":"x","expires_in":3600,"refresh_token":"AQD"}""");
        var grant = await client.ExchangeCodeAsync("the-code", "the-verifier", TestContext.Current.CancellationToken);

        Assert.Equal(("BQD", "AQD", 3600), (grant.AccessToken, grant.RefreshToken, grant.ExpiresInSeconds));
        var (request, body) = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://accounts.spotify.com/api/token", request.RequestUri?.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content?.Headers.ContentType?.MediaType);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "the-code",
                ["redirect_uri"] = "https://tape-craft.example/auth/spotify/callback",
                ["client_id"] = "clientid",
                ["code_verifier"] = "the-verifier",
            },
            QueryHelpers.ParseQuery(body).ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
    }

    [Fact]
    public async Task Reports_a_refused_exchange_by_status_only()
    {
        var (client, _) = Client(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid authorization code the-code"}""");
        var error = await Assert.ThrowsAsync<SpotifyException>(() => client.ExchangeCodeAsync("the-code", "v", TestContext.Current.CancellationToken));
        Assert.Equal("Spotify answered the token request with 400", error.Message);
    }

    [Theory]
    [InlineData("""{"access_token":"BQD","expires_in":3600}""")]
    [InlineData("""{"refresh_token":"AQD","expires_in":3600}""")]
    [InlineData("""{"access_token":"BQD","refresh_token":"AQD"}""")]
    public async Task Refuses_an_incomplete_token_answer(string body)
    {
        var (client, _) = Client(HttpStatusCode.OK, body);
        await Assert.ThrowsAsync<SpotifyException>(() => client.ExchangeCodeAsync("c", "v", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reads_the_profile_with_the_access_token()
    {
        var (client, recorder) = Client(HttpStatusCode.OK, """{"id":"ada-id","display_name":"Ada","email":"not@read.example"}""");
        var profile = await client.GetProfileAsync("BQD", TestContext.Current.CancellationToken);

        Assert.Equal(new("ada-id", "Ada"), profile);
        var (request, _) = Assert.Single(recorder.Requests);
        Assert.Equal("https://api.spotify.com/v1/me", request.RequestUri?.ToString());
        Assert.Equal("Bearer BQD", request.Headers.Authorization?.ToString());
    }

    [Theory]
    [InlineData("""{"display_name":"Ada"}""")]
    [InlineData("""{"id":" ","display_name":"Ada"}""")]
    public async Task Refuses_a_profile_without_an_id(string body)
    {
        var (client, _) = Client(HttpStatusCode.OK, body);
        await Assert.ThrowsAsync<SpotifyException>(() => client.GetProfileAsync("BQD", TestContext.Current.CancellationToken));
    }
}
