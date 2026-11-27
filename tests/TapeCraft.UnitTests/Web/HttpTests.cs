using System.Net;
using TapeCraft.Api.Web;

namespace TapeCraft.UnitTests.Web;

public sealed class HttpTests : IAsyncLifetime
{
    private readonly AppFactory _app = new();
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _client = _app.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Get(string url) => _client.GetAsync(url, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Serves_the_public_landing_page_without_touching_the_database()
    {
        _app.Probe.Answer = _ => throw new InvalidOperationException("the database is down");
        var response = await Get("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<title>tape-craft</title>", html);
        Assert.Contains("<div id=\"root\"></div>", html);
        Assert.Equal(0, _app.Probe.Calls);
    }

    [Fact]
    public async Task Health_is_200_when_the_database_answers()
    {
        var response = await Get("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Health_is_503_without_details_when_the_database_fails()
    {
        _app.Probe.Answer = _ => Task.FromException(new InvalidOperationException("password authentication failed"));
        var response = await Get("/healthz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"unavailable"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/sw.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/offline.html", "text/html")]
    [InlineData("/icons/icon-192.png", "image/png")]
    [InlineData("/icons/icon-512.png", "image/png")]
    public async Task Serves_the_app_files(string url, string type)
    {
        var response = await Get(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/../appsettings.json")]
    [InlineData("/%2e%2e/appsettings.json")]
    [InlineData("/..%2fappsettings.json")]
    [InlineData("/TapeCraft.Api.dll")]
    public async Task Serves_nothing_outside_the_app_files(string url)
    {
        var response = await Get(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Logging", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/healthz")]
    [InlineData("/missing")]
    [InlineData("/%")]
    public async Task Sets_the_security_headers(string url)
    {
        var headers = (await Get(url)).Headers;
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Assert.Single(headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(headers.GetValues("Referrer-Policy")));
        Assert.Contains("frame-ancestors 'none'", SecurityHeaders.ContentSecurityPolicy);
    }
}
