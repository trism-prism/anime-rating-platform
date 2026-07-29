using System.Text.Json;
using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

public class StatsTests
{
    static async Task<IActionResult> Call(Func<AppDbContext> open, string me, Func<StatsController, Task<IActionResult>> f)
    {
        using var db = open();
        return await f(As(new StatsController(db), me));
    }

    // alice: Title 1 (TV, 12 eps, Action+Comedy) completed in March 2025, Title 2 (MOVIE, Drama) watching 5 eps, Title 3 manga planning.
    // Bob has one completed entry and a rating that must never show up in alice's numbers.
    internal static async Task Scenario(Func<AppDbContext> open)
    {
        await Seed(open);
        using var db = open();
        var t1 = await db.Titles.FindAsync(1); t1!.Format = "TV"; t1.Episodes = 12; t1.Genres = "Action,Comedy";
        var t2 = await db.Titles.FindAsync(2); t2!.Format = "MOVIE"; t2.Genres = "Drama,Action";
        var t3 = await db.Titles.FindAsync(3); t3!.Type = TitleType.Manga; t3.Format = "MANGA"; t3.Genres = "Action"; t3.Volumes = 5;
        db.WatchList.AddRange(
            new WatchListEntry { UserId = "a", TitleId = 1, Status = WatchStatus.Completed, FinishedOn = new DateOnly(2025, 3, 10) },
            new WatchListEntry { UserId = "a", TitleId = 2, Status = WatchStatus.Watching, Progress = 5 },
            new WatchListEntry { UserId = "a", TitleId = 3, Status = WatchStatus.Planning },
            new WatchListEntry { UserId = "b", TitleId = 1, Status = WatchStatus.Completed, Progress = 12, FinishedOn = new DateOnly(2025, 3, 11) });
        db.Ratings.AddRange(
            new Rating { UserId = "a", TitleId = 1, Overall = 9, CreatedAt = new DateTime(2025, 3, 11, 0, 0, 0, DateTimeKind.Utc) },
            new Rating { UserId = "a", TitleId = 2, Overall = 6.5, CreatedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Rating { UserId = "a", TitleId = 3, Volume = 2, Overall = 7, CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Rating { UserId = "b", TitleId = 1, Overall = 1, CreatedAt = new DateTime(2025, 3, 12, 0, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();
    }

    internal static async Task AssertStats(Func<AppDbContext> open)
    {
        await Scenario(open);
        var s = Json(await Call(open, "a", c => c.All()));
        Assert.Equal(new[] { 1, 1, 1, 0 }, s.GetProperty("statusCounts").EnumerateObject().Select(p => p.Value.GetInt32()).ToArray()); // Planning, Watching, Completed, Dropped
        Assert.Equal(2, s.GetProperty("ratingCount").GetInt32()); // the volume rating is kept out
        Assert.Equal(7.75, s.GetProperty("averageScore").GetDouble());
        var dist = s.GetProperty("distribution").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 1, 0, 1, 0 }, dist); // 6.5 rounds up to 7, 9 stays
        Assert.Equal(17, s.GetProperty("episodes").GetInt32()); // 12 for the finished show, 5 so far for the other
        Assert.Equal(1, s.GetProperty("volumesRated").GetInt32());
        var genres = s.GetProperty("genres").EnumerateArray().Select(g => (g.GetProperty("name").GetString(), g.GetProperty("count").GetInt32())).ToArray();
        Assert.Equal(("Action", 3), genres[0]);
        Assert.Equal(new[] { "MANGA", "MOVIE", "TV" }, s.GetProperty("formats").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray());

        var y = Json(await Call(open, "a", c => c.Year(2025)));
        Assert.Equal((1, 12, 1), (y.GetProperty("finished").GetInt32(), y.GetProperty("episodes").GetInt32(), y.GetProperty("ratingCount").GetInt32()));
        Assert.Equal(1, y.GetProperty("byMonth")[2].GetInt32()); // March
        Assert.Equal(9, y.GetProperty("averageScore").GetDouble());
        Assert.Equal("Title 1", y.GetProperty("topRated")[0].GetProperty("name").GetString());
        var none = Json(await Call(open, "a", c => c.Year(2020)));
        Assert.Equal((0, JsonValueKind.Null), (none.GetProperty("finished").GetInt32(), none.GetProperty("averageScore").ValueKind));
        Assert.Equal(404, Status(await Call(open, "a", c => c.Year(1800))));
    }

    [Fact]
    public Task Numbers_come_from_the_callers_own_rows_only() => AssertStats(Mem());

    [Fact]
    public async Task A_new_user_gets_empty_numbers()
    {
        var open = Mem();
        await Seed(open);
        var s = Json(await Call(open, "c", c => c.All()));
        Assert.Equal((0, JsonValueKind.Null, 0), (s.GetProperty("ratingCount").GetInt32(), s.GetProperty("averageScore").ValueKind, s.GetProperty("episodes").GetInt32()));
    }
}
