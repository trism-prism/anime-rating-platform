using System.Text.Json;
using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using static api.Tests.SocialTests;

namespace api.Tests;

// ?scope=all|friends|me on the read routes that show ratings. Viewer a; b, f and g are a's accepted friends, p a pending request,
// d a friend a blocked, c a stranger. Series 1 has titles 1 and 2, series 2 has title 3.
public class RatingScopeTests
{
    static async Task<Func<AppDbContext>> Seed()
    {
        var open = ReviewTests.Mem();
        using var db = open();
        foreach (var id in new[] { "a", "b", "f", "p", "d", "c", "g" }) db.Users.Add(User(id, "user" + id));
        db.Series.AddRange(new Series { Id = 1, Name = "One", Kind = TitleType.Anime, Popularity = 1 }, new Series { Id = 2, Name = "Two", Kind = TitleType.Anime, Popularity = 2 });
        db.Titles.AddRange(
            new Title { Id = 1, AniListId = 1, Name = "T1", SeriesId = 1, Position = 1 },
            new Title { Id = 2, AniListId = 2, Name = "T2", SeriesId = 1, Position = 2 },
            new Title { Id = 3, AniListId = 3, Name = "T3", SeriesId = 2, Position = 1 });
        await db.SaveChangesAsync();
        foreach (var (x, y, st) in new[] { ("a", "b", FriendStatus.Accepted), ("a", "f", FriendStatus.Accepted), ("a", "p", FriendStatus.Pending), ("a", "d", FriendStatus.Accepted), ("a", "g", FriendStatus.Accepted) })
            db.Friendships.Add(new Friendship { UserLowId = x, UserHighId = y, RequesterId = x, Status = st });
        db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "d" });
        await db.SaveChangesAsync();
        // title 1: a 2, b 8, f 4 (OnlyMe), p 5, d 6 (blocked), c 10
        foreach (var (u, s, aud) in new[] { ("a", 2, Audience.Public), ("b", 8, Audience.Public), ("f", 4, Audience.OnlyMe), ("p", 5, Audience.Public), ("d", 6, Audience.Public), ("c", 10, Audience.Public) })
            await Rate(open, u, 1, s, aud, "review by " + u);
        return open;
    }

    // keeps the stored title totals in step, as the write route does
    static async Task Rate(Func<AppDbContext> open, string user, int title, int score, Audience aud = Audience.Public, string? text = null)
    {
        using var db = open();
        db.Ratings.Add(new Rating { UserId = user, TitleId = title, Story = score, Art = score, Characters = score, Enjoyment = score, Overall = score, Audience = aud, ReviewText = text });
        var t = await db.Titles.FindAsync(title);
        t!.RatingCount++;
        t.RatingSum += score;
        await db.SaveChangesAsync();
    }

    static async Task<IActionResult> Run<T>(Func<AppDbContext> open, string? me, Func<T, Task<IActionResult>> f) where T : ControllerBase
    {
        using var db = open();
        ControllerBase c = typeof(T) == typeof(RecommendationsController)
            ? new RecommendationsController(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))
            : (T)Activator.CreateInstance(typeof(T), db)!;
        return await f(As((T)c, me));
    }

    static double? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static async Task<(double? Avg, int Count)> TitleAvg(Func<AppDbContext> open, string? me, string? scope)
    {
        var j = Json(await Run<TitlesController>(open, me, c => c.Get(1, scope)));
        var a = j.GetProperty("averages");
        return (a.ValueKind == JsonValueKind.Null ? null : a.GetProperty("overall").GetDouble(), j.GetProperty("ratingCount").GetInt32());
    }

    [Fact]
    public async Task Title_default_counts_every_rating_whatever_its_audience()
    {
        var open = await Seed();
        Assert.Equal((5.83, 6), await TitleAvg(open, "a", null)); // 35 / 6, the OnlyMe 4 included
        Assert.Equal((5.83, 6), await TitleAvg(open, "a", "all"));
        Assert.Equal((5.83, 6), await TitleAvg(open, null, null));
    }

    [Fact]
    public async Task Title_friends_scope_counts_accepted_friends_the_viewer_may_see()
    {
        var open = await Seed();
        // f's OnlyMe 4, pending p, blocked d and stranger c are out; only b is left
        Assert.Equal((8.0, 1), await TitleAvg(open, "a", "friends"));
        Assert.Equal((8.0, 1), await TitleAvg(open, "a", "FRIENDS")); // any case
        await Rate(open, "g", 1, 6, Audience.Friends);
        Assert.Equal((7.0, 2), await TitleAvg(open, "a", "friends")); // a Friends-audience rating of a friend counts
    }

    [Fact]
    public async Task Title_me_scope_uses_only_own_ratings_and_none_gives_null_averages()
    {
        var open = await Seed();
        Assert.Equal((2.0, 1), await TitleAvg(open, "a", "me"));
        Assert.Equal((null, 0), await TitleAvg(open, "c", "friends")); // c has no friends
    }

    [Fact]
    public async Task Anonymous_friends_and_me_behave_as_all()
    {
        var open = await Seed();
        Assert.Equal((5.83, 6), await TitleAvg(open, null, "friends"));
        Assert.Equal((5.83, 6), await TitleAvg(open, null, "me"));
    }

    [Fact]
    public async Task Invalid_scope_is_400_everywhere()
    {
        var open = await Seed();
        foreach (var bad in new[] { "everyone", "1", "all,me" })
        {
            Assert.Equal(400, Status(await Run<TitlesController>(open, "a", c => c.Get(1, bad))));
            Assert.Equal(400, Status(await Run<SeriesController>(open, "a", c => c.Get(1, bad))));
            Assert.Equal(400, Status(await Run<SeriesController>(open, "a", c => c.List(null, null, null, null, null, 24, bad))));
            Assert.Equal(400, Status(await Run<RankingsController>(open, "a", c => c.Get("overall", null, 20, bad))));
            Assert.Equal(400, Status(await Run<ReviewsController>(open, "a", c => c.ForTitle(1, null, null, bad))));
            Assert.Equal(400, Status(await Run<ReviewsController>(open, "a", c => c.ForSeries(1, null, null, bad))));
            Assert.Equal(400, Status(await Run<RecommendationsController>(open, "a", c => c.ForTitle(1, bad))));
            Assert.Equal(400, Status(await Run<RecommendationsController>(open, "a", c => c.ForSeries(1, bad))));
            Assert.Equal(400, Status(await Run<ProfilesController>(open, "a", c => c.Ratings("userb", null, bad))));
            Assert.Equal(400, Status(await Run<ProfilesController>(open, "a", c => c.Overview("userb", bad))));
        }
    }

    [Fact]
    public async Task Volume_averages_follow_the_scope()
    {
        var open = await Seed();
        using (var db = open())
        {
            db.Ratings.Add(new Rating { UserId = "a", TitleId = 1, Volume = 1, Story = 3, Art = 3, Characters = 3, Enjoyment = 3, Overall = 3 });
            db.Ratings.Add(new Rating { UserId = "b", TitleId = 1, Volume = 1, Story = 9, Art = 9, Characters = 9, Enjoyment = 9, Overall = 9 });
            await db.SaveChangesAsync();
        }
        async Task<double> Vol(string? scope) =>
            Json(await Run<TitlesController>(open, "a", c => c.Get(1, scope))).GetProperty("volumeAverages")[0].GetProperty("average").GetDouble();
        Assert.Equal(6.0, await Vol(null));
        Assert.Equal(9.0, await Vol("friends"));
        Assert.Equal(3.0, await Vol("me"));
    }

    [Fact]
    public async Task Series_detail_averages_follow_the_scope()
    {
        var open = await Seed();
        await Rate(open, "b", 2, 6);
        async Task<(double? Avg, int Count)> S(string? scope)
        {
            var j = Json(await Run<SeriesController>(open, "a", c => c.Get(1, scope)));
            return (Num(j, "average"), j.GetProperty("ratingCount").GetInt32());
        }
        Assert.Equal((5.92, 7), await S(null)); // mean of the title averages 35/6 and 6
        Assert.Equal((7.0, 2), await S("friends")); // b: 8 and 6
        Assert.Equal((2.0, 1), await S("me"));
        var j = Json(await Run<SeriesController>(open, "a", c => c.Get(1, "friends")));
        Assert.Equal(new[] { 1, 1 }, j.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("ratingCount").GetInt32()).ToArray());
    }

    static async Task<string[]> Names(Func<AppDbContext> open, string? me, string? sort, string? scope, int take = 24, string? cursor = null)
    {
        var j = Json(await Run<SeriesController>(open, me, c => c.List(null, null, null, cursor, sort, take, scope)));
        return j.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToArray();
    }

    [Fact]
    public async Task Browse_scores_and_sorts_follow_the_scope_and_page_stably()
    {
        var open = await Seed();
        // friends b and g rate series 2 higher than series 1
        await Rate(open, "b", 3, 10);
        await Rate(open, "g", 3, 9);
        var j = Json(await Run<SeriesController>(open, "a", c => c.List(null, null, null, null, "top", 24, "friends")));
        var items = j.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Two", "One" }, items.Select(i => i.GetProperty("name").GetString()).ToArray());
        Assert.Equal(9.5, items[0].GetProperty("average").GetDouble());
        Assert.Equal(2, items[0].GetProperty("ratingCount").GetInt32());
        Assert.Equal(8.0, items[1].GetProperty("average").GetDouble());
        Assert.Equal(2, j.GetProperty("total").GetInt32());

        // default unchanged: every rating counts
        Assert.Equal(new[] { "One", "Two" }, await Names(open, "a", "popular", null));
        Assert.Equal(new[] { "Two", "One" }, await Names(open, "a", "top", null)); // 9.5 vs 5.83
        // me: only a's own title-1 rating, so top lists only series 1
        Assert.Equal(new[] { "One" }, await Names(open, "a", "top", "me"));
        // popular scoped: series with scoped ratings first, then the rest by popularity, in one order across pages
        Assert.Equal(new[] { "One", "Two" }, await Names(open, "a", "popular", "me"));
        var first = Json(await Run<SeriesController>(open, "a", c => c.List(null, null, null, null, "popular", 1, "me")));
        Assert.Equal(new[] { "One" }, first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());
        Assert.Equal(new[] { "Two" }, await Names(open, "a", "popular", "me", 1, first.GetProperty("nextCursor").GetString()));
        // az keeps every series but shows the scoped score
        var az = Json(await Run<SeriesController>(open, "a", c => c.List(null, null, null, null, "az", 24, "me")));
        Assert.Equal(new double?[] { 2.0, null }, az.GetProperty("items").EnumerateArray().Select(i => Num(i, "average")).ToArray());
        // anonymous friends = all
        Assert.Equal(new[] { "Two", "One" }, await Names(open, null, "top", "friends"));
    }

    static async Task<string[]> Ranked(Func<AppDbContext> open, string? me, string? scope) =>
        Json(await Run<RankingsController>(open, me, c => c.Get("overall", null, 20, scope))).EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task Rankings_need_three_ratings_in_the_scope()
    {
        var open = await Seed();
        foreach (var u in new[] { "b", "f", "g" }) await Rate(open, u, 3, 9); // three friends on series 2
        Assert.Equal(new[] { "Two", "One" }, await Ranked(open, "a", null)); // 9 vs 5.83
        Assert.Equal(new[] { "Two" }, await Ranked(open, "a", "friends")); // series 1 has one friend rating: under the minimum
        Assert.Empty(await Ranked(open, "a", "me"));
        Assert.Equal(new[] { "Two", "One" }, await Ranked(open, null, "friends")); // anonymous = all
    }

    [Fact]
    public async Task Rankings_default_still_counts_an_OnlyMe_rating()
    {
        var open = await Seed();
        // leave a, b and f (OnlyMe): three ratings only because the OnlyMe one counts
        using (var db = open()) { db.Ratings.RemoveRange(db.Ratings.Where(r => r.UserId == "p" || r.UserId == "d" || r.UserId == "c")); await db.SaveChangesAsync(); }
        Assert.Equal(new[] { "One" }, await Ranked(open, "a", null));
        Assert.Equal(new[] { "One" }, await Ranked(open, null, null));
    }

    [Fact]
    public async Task Review_lists_follow_the_scope_and_keep_paging()
    {
        var open = await Seed();
        string[] Authors(IActionResult r) => Json(r).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userName").GetString()!).ToArray();
        // viewer a by default: not f's OnlyMe, not blocked d
        Assert.Equal(new[] { "userc", "userp", "userb", "usera" }, Authors(await Run<ReviewsController>(open, "a", c => c.ForTitle(1, "newest", null))));
        Assert.Equal(new[] { "userb" }, Authors(await Run<ReviewsController>(open, "a", c => c.ForTitle(1, "newest", null, "friends"))));
        Assert.Equal(new[] { "usera" }, Authors(await Run<ReviewsController>(open, "a", c => c.ForTitle(1, "newest", null, "me"))));
        Assert.Equal(new[] { "userb" }, Authors(await Run<ReviewsController>(open, "a", c => c.ForSeries(1, null, null, "friends"))));
        // anonymous: friends = all (the public ones, a blocked pair doesn't apply)
        Assert.Equal(5, Authors(await Run<ReviewsController>(open, null, c => c.ForTitle(1, "newest", null, "friends"))).Length);

        // cursor paging inside a scope
        for (var i = 0; i < 12; i++) await Rate(open, "b", 2, 7, Audience.Public, "again " + i);
        var page1 = Json(await Run<ReviewsController>(open, "a", c => c.ForTitle(2, "newest", null, "friends")));
        Assert.Equal(10, page1.GetProperty("items").GetArrayLength());
        var cursor = page1.GetProperty("nextCursor").GetString();
        Assert.Equal(2, Authors(await Run<ReviewsController>(open, "a", c => c.ForTitle(2, "newest", cursor, "friends"))).Length);
        Assert.Empty(Authors(await Run<ReviewsController>(open, "a", c => c.ForTitle(2, "newest", null, "me"))));
    }

    [Fact]
    public async Task Also_rated_follows_the_scope()
    {
        var open = await Seed();
        // friends b and g like title 1 and also rated title 3; f's rating of title 1 is OnlyMe
        foreach (var u in new[] { "b", "g" }) await Rate(open, u, 1, 9);
        foreach (var u in new[] { "b", "g", "c" }) await Rate(open, u, 3, 8);
        int Count(IActionResult r) => Json(r).GetArrayLength();
        Assert.Equal(0, Count(await Run<RecommendationsController>(open, "a", c => c.ForTitle(1, "friends")))); // two friends: under the minimum of 3
        Assert.Equal(0, Count(await Run<RecommendationsController>(open, "a", c => c.ForTitle(1, "me"))));
        Assert.Equal(0, Count(await Run<RecommendationsController>(open, "a", c => c.ForSeries(1, "me"))));
        Assert.Equal(200, Status(await Run<RecommendationsController>(open, "a", c => c.ForTitle(1, "ME"))));
        Assert.Equal(0, Count(await Run<RecommendationsController>(open, "a", c => c.ForSeries(1, "friends"))));
        await Rate(open, "f", 1, 9, Audience.Friends);
        await Rate(open, "f", 3, 8);
        Assert.Equal(1, Count(await Run<RecommendationsController>(open, "a", c => c.ForTitle(1, "friends"))));
        Assert.Equal(1, Count(await Run<RecommendationsController>(open, "a", c => c.ForSeries(1, "friends"))));
    }

    // ---- profiles: the scope is the rating's own audience ----

    // a: title 1 Public, title 2 Friends, title 3 OnlyMe. b: title 1 Public, title 2 Friends, title 3 OnlyMe
    static async Task<Func<AppDbContext>> SeedProfile()
    {
        var open = await Seed();
        await Rate(open, "b", 2, 7, Audience.Friends, "friends only");
        await Rate(open, "b", 3, 6, Audience.OnlyMe, "private");
        await Rate(open, "a", 2, 5, Audience.Friends);
        await Rate(open, "a", 3, 4, Audience.OnlyMe);
        return open;
    }

    static async Task<int[]> Audiences(Func<AppDbContext> open, string? me, string name, string? scope)
    {
        var ids = Json(await Run<ProfilesController>(open, me, c => c.Ratings(name, null, scope))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("ratingId").GetInt32()).ToList();
        using var db = open();
        return db.Ratings.Where(x => ids.Contains(x.Id)).Select(x => (int)x.Audience).OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task Other_profile_all_is_everything_visible_and_friends_is_the_friends_audience_only()
    {
        var open = await SeedProfile();
        int pub = (int)Audience.Public, fr = (int)Audience.Friends;
        Assert.Equal(new[] { pub, fr }, await Audiences(open, "a", "userb", null)); // the OnlyMe one is hidden
        Assert.Equal(new[] { pub, fr }, await Audiences(open, "a", "userb", "all"));
        Assert.Equal(new[] { fr }, await Audiences(open, "a", "userb", "friends"));
        // a stranger doesn't see the Friends one at all
        Assert.Empty(await Audiences(open, "c", "userb", "friends"));
        Assert.Equal(new[] { pub }, await Audiences(open, "c", "userb", null));
        Assert.Equal(400, Status(await Run<ProfilesController>(open, "a", c => c.Ratings("userb", null, "me"))));
        Assert.Equal(400, Status(await Run<ProfilesController>(open, "a", c => c.Overview("userb", "me"))));
    }

    [Fact]
    public async Task Own_profile_all_friends_and_me_split_by_audience()
    {
        var open = await SeedProfile();
        Assert.Equal(new[] { 0, 1, 2 }, await Audiences(open, "a", "usera", null));
        Assert.Equal(new[] { 1 }, await Audiences(open, "a", "usera", "friends"));
        Assert.Equal(new[] { 2 }, await Audiences(open, "a", "usera", "me"));
    }

    [Fact]
    public async Task Overview_follows_the_profile_scope()
    {
        var open = await SeedProfile();
        async Task<(int Count, int Top)> O(string name, string? scope)
        {
            var j = Json(await Run<ProfilesController>(open, "a", c => c.Overview(name, scope)));
            return (j.GetProperty("ratingCount").GetInt32(), j.GetProperty("topTitles").GetArrayLength());
        }
        Assert.Equal((3, 3), await O("usera", null));
        Assert.Equal((1, 1), await O("usera", "friends"));
        Assert.Equal((1, 1), await O("usera", "me"));
        Assert.Equal((2, 2), await O("userb", null));
        Assert.Equal((1, 1), await O("userb", "friends"));
    }
}
