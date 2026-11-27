using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Spotify;

/// <summary>Spotify over HTTP. Follows the authorization code flow with PKCE, which needs no client secret.</summary>
public sealed class SpotifyClient(HttpClient http, SignInConfig config) : ISpotify
{
    public static readonly Uri AccountsBase = new("https://accounts.spotify.com/");
    public static readonly Uri ApiBase = new("https://api.spotify.com/");

    /// <summary>Only what the roadmap needs: read Liked Songs, read and save playlists (issue #3).</summary>
    public const string Scopes = "user-library-read playlist-modify-private playlist-modify-public playlist-read-private";

    public Uri AuthorizeUri(string state, string codeChallenge) => AuthorizeUri(config, state, codeChallenge);

    public static Uri AuthorizeUri(SignInConfig config, string state, string codeChallenge) =>
        new(QueryHelpers.AddQueryString(new Uri(AccountsBase, "authorize").ToString(), new Dictionary<string, string?>
        {
            ["client_id"] = config.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = config.RedirectUri.ToString(),
            ["scope"] = Scopes,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge,
            ["state"] = state,
        }));

    public async Task<SpotifyGrant> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AccountsBase, "api/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri.ToString(),
                ["client_id"] = config.ClientId,
                ["code_verifier"] = codeVerifier,
            }),
        };
        var answer = await SendAsync<TokenAnswer>(request, "token request", cancellationToken);
        if (string.IsNullOrEmpty(answer.AccessToken) || string.IsNullOrEmpty(answer.RefreshToken) || answer.ExpiresIn <= 0)
            throw new SpotifyException("Spotify's token answer is incomplete");
        return new SpotifyGrant(answer.AccessToken, answer.RefreshToken, answer.ExpiresIn);
    }

    public async Task<SpotifyProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase, "v1/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var answer = await SendAsync<ProfileAnswer>(request, "profile request", cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Id)) throw new SpotifyException("Spotify's profile has no user ID");
        return new SpotifyProfile(answer.Id, string.IsNullOrWhiteSpace(answer.DisplayName) ? null : answer.DisplayName);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        // The body of a refusal may echo the request, so only the status is reported.
        if (!response.IsSuccessStatusCode) throw new SpotifyException($"Spotify answered the {what} with {(int)response.StatusCode}");
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new SpotifyException($"Spotify answered the {what} with an empty body");
    }

    private sealed record TokenAnswer(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record ProfileAnswer(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("display_name")] string? DisplayName);
}

public sealed class SpotifyException(string message) : Exception(message);
