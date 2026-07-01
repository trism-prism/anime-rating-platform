using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class ModelTests
{
    static IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IIndex> UniqueIndexes<T>()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options;
        using var db = new AppDbContext(options);
        return db.Model.FindEntityType(typeof(T))!.GetIndexes().Where(i => i.IsUnique).ToList();
    }

    [Fact]
    public void Rating_is_unique_per_user_and_title()
    {
        var index = Assert.Single(UniqueIndexes<Rating>());
        Assert.Equal(new[] { "UserId", "TitleId" }, index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Title_AniListId_is_unique()
    {
        var index = Assert.Single(UniqueIndexes<Title>());
        Assert.Equal("AniListId", Assert.Single(index.Properties).Name);
    }
}
