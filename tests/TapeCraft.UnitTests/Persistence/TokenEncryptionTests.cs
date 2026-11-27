using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TapeCraft.Api.Infrastructure.Persistence;
using TapeCraft.Domain;

namespace TapeCraft.UnitTests.Persistence;

public sealed class TokenEncryptionTests
{
    private static TapeCraftDbContext Context(IDataProtectionProvider protection)
    {
        var options = new DbContextOptionsBuilder<TapeCraftDbContext>();
        PersistenceSetup.Configure(options, Npgsql.NpgsqlDataSource.Create("Host=localhost"));
        return new TapeCraftDbContext(options.Options, protection);
    }

    [Fact]
    public void Encrypts_both_spotify_tokens_and_reads_them_back()
    {
        var protection = new EphemeralDataProtectionProvider();
        using var db = Context(protection);
        var entity = db.Model.FindEntityType(typeof(Token))!;

        foreach (var name in new[] { nameof(Token.AccessToken), nameof(Token.RefreshToken) })
        {
            var converter = entity.FindProperty(name)!.GetValueConverter();
            Assert.NotNull(converter);
            var stored = (string)converter.ConvertToProvider("BQD-secret-token")!;
            Assert.DoesNotContain("BQD-secret-token", stored);
            Assert.Equal("BQD-secret-token", converter.ConvertFromProvider(stored));
        }
    }

    [Fact]
    public void Gives_each_token_column_a_unique_owner()
    {
        using var db = Context(new EphemeralDataProtectionProvider());
        Assert.True(db.Model.FindEntityType(typeof(User))!.GetIndexes().Single().IsUnique);
        Assert.True(db.Model.FindEntityType(typeof(Token))!.GetIndexes().Single().IsUnique);
    }
}
