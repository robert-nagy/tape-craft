namespace TapeCraft.Api.Infrastructure.Spotify;

public static class SpotifySetup
{
    public static IServiceCollection AddSpotify(this IServiceCollection services)
    {
        services.AddHttpClient<ISpotify, SpotifyClient>(http => http.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }
}
