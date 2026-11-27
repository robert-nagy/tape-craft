using System.Net;
using Npgsql;

namespace TapeCraft.Api;

/// <summary>The app's settings, read from environment variables.</summary>
public sealed record AppConfig(string ConnectionString, int Port, string Host)
{
    /// <summary>Error messages never include a value, because DATABASE_URL carries a password.</summary>
    public static AppConfig Load(IConfiguration configuration) =>
        new(ParseDatabaseUrl(configuration["DATABASE_URL"]), ParsePort(configuration["PORT"]), ParseHost(configuration["HOST"]));

    private static string ParseDatabaseUrl(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) throw new AppConfigException("DATABASE_URL is required");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("postgres" or "postgresql"))
            throw new AppConfigException("DATABASE_URL must start with postgres:// or postgresql://");
        if (string.IsNullOrEmpty(url.Host)) throw new AppConfigException("DATABASE_URL must name a host");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = url.DnsSafeHost,
            Port = url.IsDefaultPort || url.Port < 0 ? 5432 : url.Port,
            Timeout = 2,
        };

        var userInfo = url.UserInfo;
        if (userInfo.Length > 0)
        {
            var colon = userInfo.IndexOf(':');
            builder.Username = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo[..colon]);
            if (colon >= 0) builder.Password = Uri.UnescapeDataString(userInfo[(colon + 1)..]);
        }

        var database = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/'));
        if (database.Length > 0) builder.Database = database;

        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] != "sslmode" || parts.Length != 2 || !Enum.TryParse<SslMode>(parts[1], ignoreCase: true, out var sslMode))
                throw new AppConfigException("DATABASE_URL accepts only the sslmode query parameter, set to a valid mode");
            builder.SslMode = sslMode;
        }

        return builder.ConnectionString;
    }

    private static int ParsePort(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 3000;
        if (value.Length > 5 || !value.All(char.IsAsciiDigit) || int.Parse(value) > 65535)
            throw new AppConfigException("PORT must be a whole number from 0 to 65535");
        return int.Parse(value);
    }

    private static string ParseHost(string? value)
    {
        value = value?.Trim();
        // Kestrel listens on two addresses for "localhost"; one address is simpler to reason about.
        if (string.IsNullOrEmpty(value) || value == "localhost") return "127.0.0.1";
        // Kestrel listens on every interface for any other host name, so accept addresses only.
        if (!IPAddress.TryParse(value.Trim('[', ']'), out var address))
            throw new AppConfigException("HOST must be an IP address, such as 127.0.0.1 or 0.0.0.0");
        return address.ToString();
    }
}

public sealed class AppConfigException(string message) : Exception(message);
