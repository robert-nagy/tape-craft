using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Persistence;

/// <summary>Keeps who signed in and the tokens Spotify granted them.</summary>
public interface ISignInStore
{
    /// <summary>Creates the listener on their first sign-in, or updates them and replaces their tokens.</summary>
    Task<User> SaveSignInAsync(SpotifyProfile profile, SpotifyGrant grant, DateTimeOffset now, CancellationToken cancellationToken);
}
