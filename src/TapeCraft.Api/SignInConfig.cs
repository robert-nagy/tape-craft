using System.Net;

namespace TapeCraft.Api;

/// <summary>
/// The Spotify app and the Data Protection key ring, read from environment variables.
/// Only the web server needs them; <c>migrate</c> does not read them.
/// </summary>
public sealed record SignInConfig(string ClientId, Uri RedirectUri, string DataProtectionKeysDirectory)
{
    public const string CallbackPath = "/auth/spotify/callback";

    /// <summary>Error messages never include a value.</summary>
    public static SignInConfig Load(IConfiguration configuration) =>
        new(ParseClientId(configuration["SPOTIFY_CLIENT_ID"]),
            ParseRedirectUri(configuration["SPOTIFY_REDIRECT_URI"]),
            ParseKeysDirectory(configuration["DATA_PROTECTION_KEYS_DIR"]));

    private static string ParseClientId(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) throw new AppConfigException("SPOTIFY_CLIENT_ID is required");
        // It goes into the authorize URL and the token request; Spotify's IDs are hexadecimal.
        if (!value.All(char.IsAsciiLetterOrDigit))
            throw new AppConfigException("SPOTIFY_CLIENT_ID must contain only letters and digits");
        return value;
    }

    private static Uri ParseRedirectUri(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) throw new AppConfigException("SPOTIFY_REDIRECT_URI is required");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AppConfigException("SPOTIFY_REDIRECT_URI must be an absolute http or https URL");
        // Spotify accepts plain HTTP only for loopback IP addresses, not for "localhost".
        if (uri.Scheme == "http" && !(IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address)))
            throw new AppConfigException("SPOTIFY_REDIRECT_URI must use https unless it points to 127.0.0.1 or [::1]");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AppConfigException("SPOTIFY_REDIRECT_URI must not have a query or fragment");
        if (uri.AbsolutePath != CallbackPath)
            throw new AppConfigException($"SPOTIFY_REDIRECT_URI must end with {CallbackPath}");
        return uri;
    }

    private static string ParseKeysDirectory(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) throw new AppConfigException("DATA_PROTECTION_KEYS_DIR is required");
        if (!Path.IsPathFullyQualified(value)) throw new AppConfigException("DATA_PROTECTION_KEYS_DIR must be an absolute path");
        return value;
    }
}
