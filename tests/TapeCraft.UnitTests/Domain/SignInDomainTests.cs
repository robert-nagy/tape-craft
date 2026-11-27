using TapeCraft.Domain;

namespace TapeCraft.UnitTests.Domain;

public sealed class SignInDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registers_a_listener_from_their_spotify_profile()
    {
        var user = User.Register(new SpotifyProfile("spotify-id", "Ada"), Now);
        Assert.Equal(("spotify-id", "Ada", Now), (user.SpotifyUserId, user.DisplayName, user.CreatedAt));
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Refuses_a_profile_without_a_spotify_user_id(string id) =>
        Assert.Throws<ArgumentException>(() => User.Register(new SpotifyProfile(id, "Ada"), Now));

    [Fact]
    public void Takes_the_new_display_name_on_a_later_sign_in()
    {
        var user = User.Register(new SpotifyProfile("spotify-id", "Ada"), Now);
        user.UpdateProfile(new SpotifyProfile("spotify-id", "Ada L."));
        Assert.Equal("Ada L.", user.DisplayName);
    }

    [Fact]
    public void Refuses_a_profile_of_another_spotify_user()
    {
        var user = User.Register(new SpotifyProfile("spotify-id", "Ada"), Now);
        Assert.Throws<ArgumentException>(() => user.UpdateProfile(new SpotifyProfile("someone-else", "Eve")));
    }

    [Fact]
    public void Issues_a_token_that_expires_after_the_granted_lifetime()
    {
        var userId = Guid.CreateVersion7();
        var token = Token.Issue(userId, new SpotifyGrant("access", "refresh", 3600), Now);
        Assert.Equal((userId, "access", "refresh", Now.AddHours(1)), (token.UserId, token.AccessToken, token.RefreshToken, token.ExpiresAt));
    }

    [Fact]
    public void Replaces_the_tokens_on_a_later_sign_in()
    {
        var token = Token.Issue(Guid.CreateVersion7(), new SpotifyGrant("access", "refresh", 3600), Now);
        token.Replace(new SpotifyGrant("access-2", "refresh-2", 60), Now.AddDays(1));
        Assert.Equal(("access-2", "refresh-2", Now.AddDays(1).AddMinutes(1)), (token.AccessToken, token.RefreshToken, token.ExpiresAt));
    }

    [Theory]
    [InlineData("", "refresh", 3600)]
    [InlineData("access", "", 3600)]
    [InlineData("access", "refresh", 0)]
    public void Refuses_an_incomplete_grant(string access, string refresh, int expiresIn) =>
        Assert.Throws<ArgumentException>(() => Token.Issue(Guid.CreateVersion7(), new SpotifyGrant(access, refresh, expiresIn), Now));
}
