using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

public class RecommendationTests
{
    static RecommendationsController Controller(AppDbContext db, IMemoryCache? cache = null) =>
        new(db, cache ?? new MemoryCache(new MemoryCacheOptions()));

    // Title 1 (series 1) is liked (7+) by u1..u4; u5 gave it a 5. Title 2 (series 1) is not co-rated against title 1's own series.
    // Title 4 (series 2): u1, u2, u3 rated it, u1's rating is Only me (aggregates still count it).
    // Title 5 (series 3): only u1 and u2. Title 6 (series 4): u5, u6, u7, but u5 is no fan and u6, u7 never rated title 1.
    // Title 7 (series 5): u1, u2, u3 rated only a volume, which never counts.
    internal static async Task Scenario(Func<AppDbContext> open)
    {
        using var db = open();
        for (var u = 1; u <= 7; u++) db.Users.Add(new AppUser { Id = "u" + u, UserName = "user" + u, NormalizedUserName = "USER" + u });
        db.Series.AddRange(new Series { Id = 1, Name = "S1" }, new Series { Id = 2, Name = "S2" }, new Series { Id = 3, Name = "S3" }, new Series { Id = 4, Name = "S4" }, new Series { Id = 5, Name = "S5" });
        db.Titles.AddRange(
            new Title { Id = 1, AniListId = 1, Name = "T1", SeriesId = 1 }, new Title { Id = 2, AniListId = 2, Name = "T2", SeriesId = 1 },
            new Title { Id = 4, AniListId = 4, Name = "T4", SeriesId = 2 }, new Title { Id = 5, AniListId = 5, Name = "T5", SeriesId = 3 },
            new Title { Id = 6, AniListId = 6, Name = "T6", SeriesId = 4 }, new Title { Id = 7, AniListId = 7, Name = "T7", SeriesId = 5, Volumes = 3 });
        await db.SaveChangesAsync();
        void Rate(string user, int title, double overall, int? volume = null, Audience a = Audience.Public) =>
            db.Ratings.Add(new Rating { UserId = user, TitleId = title, Overall = overall, Volume = volume, Audience = a });
        foreach (var u in new[] { "u1", "u2", "u3", "u4" }) Rate(u, 1, 9);
        Rate("u5", 1, 5);
        Rate("u1", 4, 8, a: Audience.OnlyMe); Rate("u2", 4, 6); Rate("u3", 4, 10);
        Rate("u1", 5, 8); Rate("u2", 5, 8);
        Rate("u5", 6, 9); Rate("u6", 6, 9); Rate("u7", 6, 9);
        foreach (var u in new[] { "u1", "u2", "u3" }) Rate(u, 7, 9, volume: 1);
        await db.SaveChangesAsync();
    }

    internal static async Task AssertRecommendations(Func<AppDbContext> open)
    {
        await Scenario(open);
        using var db = open();
        var c = Controller(db);

        var t = Json(await c.ForTitle(1));
        Assert.Equal(1, t.GetArrayLength()); // title 5 has two co-raters, title 6 none from the fans, title 7 only volume ratings
        Assert.Equal((4, 3, 8.0), (t[0].GetProperty("titleId").GetInt32(), t[0].GetProperty("coRaters").GetInt32(), t[0].GetProperty("average").GetDouble())); // the Only me rating counts in the total
        // nothing that says who: only the title, its series, a count and an average
        Assert.Equal(new[] { "average", "coRaters", "coverUrl", "name", "seriesId", "titleId" }, t[0].EnumerateObject().Select(p => p.Name).Order().ToArray());

        var s = Json(await c.ForSeries(1));
        Assert.Equal(new[] { 2 }, s.EnumerateArray().Select(x => x.GetProperty("seriesId").GetInt32()).ToArray());

        Assert.Equal(404, Status(await c.ForTitle(99)));
        Assert.Equal(404, Status(await c.ForSeries(99)));
        Assert.Equal(0, Json(await c.ForTitle(6)).GetArrayLength()); // a title nobody liked gives nothing
    }

    [Fact]
    public Task Only_titles_with_three_or_more_co_raters_show_and_nobody_is_named() => AssertRecommendations(Mem());

    [Fact]
    public async Task Answers_are_cached()
    {
        var open = Mem();
        await Scenario(open);
        var cache = new MemoryCache(new MemoryCacheOptions());
        using (var db = open()) Assert.Equal(1, Json(await Controller(db, cache).ForTitle(1)).GetArrayLength());
        using (var db = open())
        {
            db.Ratings.RemoveRange(db.Ratings);
            await db.SaveChangesAsync();
            Assert.Equal(1, Json(await Controller(db, cache).ForTitle(1)).GetArrayLength()); // still the cached answer
            Assert.Equal(0, Json(await Controller(db).ForTitle(1)).GetArrayLength()); // a fresh cache sees the change
        }
    }
}
