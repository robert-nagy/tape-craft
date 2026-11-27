using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace TapeCraft.Api.Features.SignIn;

/// <summary>
/// One trip to Spotify and back: the random <see cref="State"/> and the PKCE <see cref="CodeVerifier"/>.
/// The browser keeps them in an encrypted, time-limited cookie until Spotify returns.
/// </summary>
public sealed record SignInAttempt(string State, string CodeVerifier)
{
    public const string CookieName = "tapecraft.signin";
    public const string CookiePath = "/auth/spotify";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string Purpose = "TapeCraft.SignInAttempt";

    /// <summary>S256: the base64url SHA-256 hash of the verifier (RFC 7636).</summary>
    public string CodeChallenge => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier)));

    public static SignInAttempt Start() =>
        // 32 bytes give a 43-character state; 64 bytes an 86-character verifier (RFC 7636 allows 43 to 128).
        new(WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64)));

    /// <summary>True only for exactly this attempt's state, compared in constant time.</summary>
    public bool Matches(string? state) =>
        !string.IsNullOrEmpty(state)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(State));

    public string Protect(IDataProtectionProvider protection) =>
        Protector(protection).Protect(JsonSerializer.Serialize(this), Lifetime);

    /// <summary>The attempt in <paramref name="cookie"/>, or null when it is missing, expired or not ours.</summary>
    public static SignInAttempt? Unprotect(IDataProtectionProvider protection, string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return null;
        try
        {
            var attempt = JsonSerializer.Deserialize<SignInAttempt>(Protector(protection).Unprotect(cookie));
            return attempt is { State.Length: > 0, CodeVerifier.Length: > 0 } ? attempt : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax, // Lax, not Strict: Spotify returns with a top-level navigation from its own site.
        Path = CookiePath,
        MaxAge = Lifetime,
        IsEssential = true,
    };

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider protection) =>
        protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    // Never print the verifier.
    public override string ToString() => "SignInAttempt";
}
