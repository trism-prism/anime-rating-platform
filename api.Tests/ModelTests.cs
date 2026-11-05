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
        // whole-title ratings (Volume null) and per-volume ratings each have their own filtered unique index
        var indexes = UniqueIndexes<Rating>();
        Assert.Equal(2, indexes.Count);
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "TitleId" }) && i.GetFilter() == "\"Volume\" IS NULL");
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "TitleId", "Volume" }) && i.GetFilter() == "\"Volume\" IS NOT NULL");
    }

    [Fact]
    public void Title_AniListId_is_unique()
    {
        var index = Assert.Single(UniqueIndexes<Title>());
        Assert.Equal("AniListId", Assert.Single(index.Properties).Name);
    }
}
