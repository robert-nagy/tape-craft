using Microsoft.Extensions.Configuration;
using Npgsql;
using TapeCraft.Api;

namespace TapeCraft.UnitTests.Configuration;

public sealed class AppConfigTests
{
    private const string Url = "postgres://tape:s%40cret@db.example:6543/tapecraft";

    private static AppConfig Load(string? databaseUrl = Url, string? port = null, string? host = null) =>
        AppConfig.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATABASE_URL"] = databaseUrl, ["PORT"] = port, ["HOST"] = host })
            .Build());

    private static string ErrorFor(string? databaseUrl = Url, string? port = null) =>
        Assert.Throws<AppConfigException>(() => Load(databaseUrl, port)).Message;

    [Fact]
    public void Turns_the_database_url_into_a_connection_string_with_a_short_timeout()
    {
        var connection = new NpgsqlConnectionStringBuilder(Load().ConnectionString);
        Assert.Equal(("db.example", 6543, "tape", "s@cret", "tapecraft", 2),
            (connection.Host, connection.Port, connection.Username, connection.Password, connection.Database, connection.Timeout));
    }

    [Theory]
    [InlineData("postgresql://db.example/tapecraft", 5432, null)]
    [InlineData("postgres://db.example:5432", 5432, null)]
    [InlineData("postgres://[::1]:5433/x", 5433, "::1")]
    public void Accepts_both_schemes_and_defaults_the_port(string url, int port, string? host)
    {
        var connection = new NpgsqlConnectionStringBuilder(Load(url).ConnectionString);
        Assert.Equal(port, connection.Port);
        Assert.Equal(host ?? "db.example", connection.Host);
    }

    [Fact]
    public void Reads_sslmode()
    {
        var connection = new NpgsqlConnectionStringBuilder(Load(Url + "?sslmode=require").ConnectionString);
        Assert.Equal(SslMode.Require, connection.SslMode);
    }

    [Fact]
    public void Applies_defaults_for_port_and_host()
    {
        var config = Load();
        Assert.Equal((3000, "127.0.0.1"), (config.Port, config.Host));
    }

    [Theory]
    [InlineData("8080", "0.0.0.0", 8080, "0.0.0.0")]
    [InlineData("0", "localhost", 0, "127.0.0.1")]
    public void Reads_port_and_host(string port, string host, int expectedPort, string expectedHost)
    {
        var config = Load(port: port, host: host);
        Assert.Equal((expectedPort, expectedHost), (config.Port, config.Host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_the_database_url(string? value) =>
        Assert.Equal("DATABASE_URL is required", ErrorFor(value));

    [Theory]
    [InlineData("mysql://user:hunter2@localhost/x", "must start with postgres")]
    [InlineData("user:hunter2@localhost", "must start with postgres")]
    [InlineData("postgres://user:hunter2@db.example/x?password=hunter2", "accepts only the sslmode")]
    [InlineData("postgres://user:hunter2@db.example/x?sslmode=hunter2", "accepts only the sslmode")]
    public void Rejects_a_bad_database_url_without_echoing_it(string value, string reason)
    {
        var message = ErrorFor(value);
        Assert.Contains(reason, message);
        Assert.DoesNotContain("hunter2", message);
    }

    [Theory]
    [InlineData("my host")]
    [InlineData("a/b")]
    [InlineData("http://0.0.0.0")]
    [InlineData("app.internal")]
    public void Rejects_a_bad_host(string value) =>
        Assert.Equal("HOST must be an IP address, such as 127.0.0.1 or 0.0.0.0",
            Assert.Throws<AppConfigException>(() => Load(host: value)).Message);

    [Theory]
    [InlineData("::", "::")]
    [InlineData(" 0.0.0.0 ", "0.0.0.0")]
    [InlineData("[::1]", "::1")]
    public void Accepts_addresses(string value, string expected) =>
        Assert.Equal(expected, Load(host: value).Host);

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("3000.5")]
    [InlineData("0x50")]
    [InlineData("999999999999")]
    [InlineData("٣٠٠٠")]
    public void Rejects_a_bad_port(string value) =>
        Assert.Contains("PORT must be", ErrorFor(port: value));
}
