using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Persistence;

public sealed class TapeCraftDbContext(DbContextOptions<TapeCraftDbContext> options, IDataProtectionProvider protection)
    : DbContext(options)
{
    /// <summary>The Data Protection purpose for Spotify tokens at rest. Changing it makes stored tokens unreadable.</summary>
    public const string TokenPurpose = "TapeCraft.Tokens";

    /// <summary>Encrypts the tokens. Part of the model cache key, so that each key ring gets its own converters.</summary>
    internal IDataProtectionProvider Protection { get; } = protection;

    public DbSet<User> Users => Set<User>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Analysis> Analysis => Set<Analysis>();
    public DbSet<Playlist> Playlists => Set<Playlist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.SpotifyUserId).IsRequired();
            user.HasIndex(u => u.SpotifyUserId).IsUnique();
        });

        // Encrypted on write and decrypted on read, so neither token is ever stored in clear text.
        var protector = Protection.CreateProtector(TokenPurpose);
        var encrypted = new ValueConverter<string, string>(plain => protector.Protect(plain), stored => protector.Unprotect(stored));
        modelBuilder.Entity<Token>(token =>
        {
            token.Property(t => t.AccessToken).HasConversion(encrypted).IsRequired();
            token.Property(t => t.RefreshToken).HasConversion(encrypted).IsRequired();
            token.HasOne<User>().WithOne().HasForeignKey<Token>(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// EF Core builds the model once per cache key. The default key is the context type, which would keep the
/// converters, and so the key ring, of the first context built in the process.
/// </summary>
internal sealed class ProtectionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TapeCraftDbContext tapeCraft ? (context.GetType(), tapeCraft.Protection, designTime) : (context.GetType(), designTime);
}
