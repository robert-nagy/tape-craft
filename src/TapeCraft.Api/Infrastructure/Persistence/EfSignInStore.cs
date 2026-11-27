using Microsoft.EntityFrameworkCore;
using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Persistence;

public sealed class EfSignInStore(TapeCraftDbContext db) : ISignInStore
{
    public async Task<User> SaveSignInAsync(SpotifyProfile profile, SpotifyGrant grant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.SpotifyUserId == profile.Id, cancellationToken);
        if (user is null)
        {
            user = User.Register(profile, now);
            db.Users.Add(user);
        }
        else
        {
            user.UpdateProfile(profile);
        }

        var token = await db.Tokens.SingleOrDefaultAsync(t => t.UserId == user.Id, cancellationToken);
        if (token is null) db.Tokens.Add(Token.Issue(user.Id, grant, now));
        else token.Replace(grant, now);

        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
