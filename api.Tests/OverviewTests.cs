using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// users/{name}/overview: top titles, top genres by average, rating count. Users: a alice, b Bob (a's friend), c carol, d dave (admin).
public class OverviewTests
{
    static DateTime Day(int d) => new(2025, 1, d, 0, 0, 0, DateTimeKind.Utc);

    // titles 1..3 get genres; ratings are added by Rate
    static async Task Setup(Func<AppDbContext> open, params string[] genres)
    {
        await Seed(open);
        using var db = open();
        for (var i = 0; i < genres.Length; i++) (await db.Titles.FindAsync(i + 1))!.Genres = genres[i];
        await db.SaveChangesAsync();
    }

    static async Task<int> Rate(Func<AppDbContext> open, string user, int title, double overall, int day, Audience a = Audience.Public, string? text = null, int? volume = null, bool spoiler = false)
    {
        using var db = open();
        var r = new Rating { UserId = user, TitleId = title, Overall = overall, CreatedAt = Day(day), Audience = a, ReviewText = text, Volume = volume, Spoiler = spoiler };
        db.Ratings.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    static async Task<System.Text.Json.JsonElement> Over(Func<AppDbContext> open, string? viewer, string name) =>
        Json(await CallP(open, viewer, p => p.Overview(name)));

    [Fact]
    public async Task Top_titles_are_by_score_then_newest_and_carry_the_review()
    {
        var open = Mem();
        await Setup(open, "Action", "Action", "Action");
        await Rate(open, "a", 1, 9, 1);
        var newer = await Rate(open, "a", 2, 9, 5, text: "great");
        await Rate(open, "a", 3, 7, 9, text: "secret", spoiler: true);

        var o = await Over(open, "a", "alice");
        Assert.Equal(3, o.GetProperty("ratingCount").GetInt32());
        var top = o.GetProperty("topTitles").EnumerateArray().ToList();
        Assert.Equal(new[] { 2, 1, 3 }, top.Select(t => t.GetProperty("titleId").GetInt32())); // the tie goes to the newer rating
        Assert.Equal(newer, top[0].GetProperty("ratingId").GetInt32());
        Assert.Equal("great", top[0].GetProperty("snippet").GetString());
        Assert.False(top[1].GetProperty("hasReview").GetBoolean());
        Assert.True(top[2].GetProperty("hasReview").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, top[2].GetProperty("snippet").ValueKind); // a spoiler sends no text
    }

    [Fact]
    public async Task Only_five_titles_and_volume_ratings_count_but_do_not_rank()
    {
        var open = Mem();
        await Setup(open, "Action");
        using (var db = open())
        {
            for (var i = 0; i < 6; i++) db.Titles.Add(new Title { Id = 10 + i, AniListId = 10 + i, Name = "T" + i, SeriesId = 1, Position = 10 + i });
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 6; i++) await Rate(open, "a", 10 + i, 5 + i * 0.5, 1);
        await Rate(open, "a", 1, 10, 1, volume: 2);

        var o = await Over(open, "a", "alice");
        Assert.Equal(7, o.GetProperty("ratingCount").GetInt32());
        Assert.Equal(5, o.GetProperty("topTitles").GetArrayLength());
        Assert.Equal(7.5, o.GetProperty("topTitles")[0].GetProperty("overall").GetDouble());
    }

    [Fact]
    public async Task Genres_rank_by_average_not_count_and_need_two_ratings()
    {
        var open = Mem();
        await Setup(open, "Action,Drama", "Action,Comedy", "Action");
        using (var db = open())
        {
            db.Titles.Add(new Title { Id = 4, AniListId = 4, Name = "T4", SeriesId = 1, Position = 4, Genres = "Drama,Horror" });
            await db.SaveChangesAsync();
        }
        await Rate(open, "a", 1, 5, 1);   // Action 5, Drama 5
        await Rate(open, "a", 2, 6, 1);   // Action 6, Comedy 6
        await Rate(open, "a", 3, 4, 1);   // Action 4 -> Action avg 5 over 3 ratings
        await Rate(open, "a", 4, 9, 1);   // Drama 9, Horror 9 -> Drama avg 7 over 2

        var g = (await Over(open, "a", "alice")).GetProperty("topGenres").EnumerateArray().Select(x => (x.GetProperty("name").GetString()!, x.GetProperty("average").GetDouble())).ToList();
        // Drama (7) beats Action (5) though Action has more ratings; Horror and Comedy have one rating, so they only fill the last place
        Assert.Equal(new[] { ("Drama", 7.0), ("Action", 5.0), ("Horror", 9.0) }, g);
    }

    [Fact]
    public async Task Genres_fall_back_to_single_ratings_when_none_qualify()
    {
        var open = Mem();
        await Setup(open, "Action", "Drama");
        await Rate(open, "a", 1, 6, 1);
        await Rate(open, "a", 2, 8, 1);
        var g = (await Over(open, "a", "alice")).GetProperty("topGenres").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "Drama", "Action" }, g);

        var none = await Over(open, "a", "carol");
        Assert.Equal(0, none.GetProperty("ratingCount").GetInt32());
        Assert.Equal(0, none.GetProperty("topTitles").GetArrayLength());
        Assert.Equal(0, none.GetProperty("topGenres").GetArrayLength());
    }

    [Fact]
    public async Task Each_viewer_sees_only_the_ratings_their_audience_allows()
    {
        var open = Mem();
        await Setup(open, "Action", "Action", "Action");
        await Rate(open, "a", 1, 9, 1);
        await Rate(open, "a", 2, 8, 1, Audience.Friends, "friends only");
        await Rate(open, "a", 3, 7, 1, Audience.OnlyMe);

        foreach (var (viewer, count) in new (string?, int)[] { ("a", 3), ("d", 3), ("b", 2), ("c", 1), (null, 1) })
        {
            var o = await Over(open, viewer, "alice");
            Assert.Equal(count, o.GetProperty("ratingCount").GetInt32());
            Assert.Equal(count, o.GetProperty("topTitles").GetArrayLength());
        }
        // the hidden ratings also stay out of the genre average
        Assert.Equal(9, (await Over(open, null, "alice")).GetProperty("topGenres")[0].GetProperty("average").GetDouble());
    }

    [Fact]
    public async Task Blocked_viewer_deleted_and_unknown_users_are_all_404()
    {
        var open = Mem();
        await Setup(open, "Action");
        await Rate(open, "a", 1, 9, 1);
        using (var db = open())
        {
            db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "c" });
            (await db.Users.FindAsync("b"))!.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        Assert.Equal(404, Status(await CallP(open, "c", p => p.Overview("alice"))));
        Assert.Equal(404, Status(await CallP(open, "a", p => p.Overview("Bob"))));
        Assert.Equal(404, Status(await CallP(open, "a", p => p.Overview("nobody"))));
    }
}
