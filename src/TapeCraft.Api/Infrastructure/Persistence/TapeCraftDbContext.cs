using Microsoft.EntityFrameworkCore;
using TapeCraft.Domain;

namespace TapeCraft.Api.Infrastructure.Persistence;

public sealed class TapeCraftDbContext(DbContextOptions<TapeCraftDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Analysis> Analysis => Set<Analysis>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
}
