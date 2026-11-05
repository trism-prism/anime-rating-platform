using System.Text.Json;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static api.Tests.SocialTests;

namespace api.Tests;

// audiences, reviews, replies, reactions, profile ratings and admin removal. The scenarios take a context factory so the
// Postgres tests (ReviewPostgresTests) run the same ones on the real database, where the queries are translated to SQL.
public class ReviewTests
{
    internal static Func<AppDbContext> Mem()
    {
        var name = Guid.NewGuid().ToString();
        return () => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
    }

    // users: a alice (author), b Bob (a's friend), c carol (stranger), d dave (admin). Series 1 with titles 1..3.
    internal static async Task Seed(Func<AppDbContext> open)
    {
        using var db = open();
        foreach (var (id, name) in new[] { ("a", "alice"), ("b", "Bob"), ("c", "carol"), ("d", "dave") })
        {
            var u = User(id, name);
            u.EmailConfirmed = true;
            db.Users.Add(u);
        }
        db.Roles.Add(new IdentityRole { Id = "admin-role", Name = "Admin", NormalizedName = "ADMIN" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "d", RoleId = "admin-role" });
        db.Series.Add(new Series { Id = 1, Name = "Show", Kind = TitleType.Anime });
        for (var t = 1; t <= 3; t++) db.Titles.Add(new Title { Id = t, AniListId = t, Name = "Title " + t, SeriesId = 1, Position = t });
        await db.SaveChangesAsync();
        db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "b", RequesterId = "a", Status = FriendStatus.Accepted });
        await db.SaveChangesAsync();
    }

    internal static async Task<int> AddRating(Func<AppDbContext> open, string user, int title, string? text, Audience audience = Audience.Public,
        int? volume = null, bool spoiler = false, int score = 0)
    {
        using var db = open();
        var r = new Rating { UserId = user, TitleId = title, Volume = volume, Story = 8, Art = 8, Characters = 8, Enjoyment = 8, Overall = 8, ReviewText = text, Audience = audience, Spoiler = spoiler, Score = score };
        db.Ratings.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    static Task Block(Func<AppDbContext> open, string blocker, string blocked) => Do(open, db => { db.UserBlocks.Add(new UserBlock { BlockerId = blocker, BlockedId = blocked }); });

    static async Task Do(Func<AppDbContext> open, Action<AppDbContext> change)
    {
        using var db = open();
        change(db);
        await db.SaveChangesAsync();
    }

    // each call gets its own context, like each request does
    internal static async Task<IActionResult> Call(Func<AppDbContext> open, string? me, Func<ReviewsController, Task<IActionResult>> f)
    {
        using var db = open();
        return await f(As(new ReviewsController(db), me));
    }

    internal static async Task<IActionResult> CallP(Func<AppDbContext> open, string? me, Func<ProfilesController, Task<IActionResult>> f)
    {
        using var db = open();
        return await f(As(new ProfilesController(db), me));
    }

    static List<int> Ids(IActionResult r) => Json(r).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
    // a profile that 404s (the owner blocked the viewer) shows no ratings
    static List<int> RatingIds(IActionResult r) => Status(r) == 404 ? new List<int>() : Json(r).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("ratingId").GetInt32()).ToList();

    // ---- the viewer rule on every read path ----

    static readonly (string? Id, string Role)[] Viewers = { ("a", "author"), ("b", "friend"), ("c", "stranger"), (null, "anonymous"), ("d", "admin") };

    static bool Sees(string role, Audience a) => role switch
    {
        "author" or "admin" => true,
        "friend" => a != Audience.OnlyMe,
        _ => a == Audience.Public
    };

    // one review per audience by alice on titles 1..3; every viewer against every read path
    internal static async Task ViewerRuleOnEveryReadPath(Func<AppDbContext> open)
    {
        await Seed(open);
        var ids = new Dictionary<Audience, int>();
        foreach (var a in Enum.GetValues<Audience>()) ids[a] = await AddRating(open, "a", (int)a + 1, "review " + a, a);

        foreach (var (viewer, role) in Viewers)
            foreach (var a in Enum.GetValues<Audience>())
            {
                var want = Sees(role, a);
                var who = $"{role} / {a}";
                Assert.True(want == Ids(await Call(open, viewer, c => c.ForTitle((int)a + 1, null, null))).Contains(ids[a]), "title list: " + who);
                Assert.True(want == Ids(await Call(open, viewer, c => c.ForSeries(1, "newest", null))).Contains(ids[a]), "series list: " + who);
                Assert.Equal(want ? 200 : 404, Status(await Call(open, viewer, c => c.One(ids[a]))));
                Assert.Equal(want ? 200 : 404, Status(await Call(open, viewer, c => c.Replies(ids[a], null))));
                Assert.True(want == RatingIds(await CallP(open, viewer, p => p.Ratings("alice", null))).Contains(ids[a]), "profile list: " + who);
            }

        // writes: a reaction or a reply needs the same visibility; the author can't react to their own review
        foreach (var (viewer, role) in Viewers.Where(v => v.Id != null))
            foreach (var a in Enum.GetValues<Audience>())
            {
                var want = Sees(role, a);
                var reaction = await Call(open, viewer, c => c.React(ids[a], new ReactionRequest("Helpful")));
                Assert.Equal(role == "author" ? 400 : want ? 200 : 404, Status(reaction));
                var reply = await Call(open, viewer, c => c.PostReply(ids[a], new ReplyRequest("hi", false)));
                Assert.Equal(want ? 201 : 404, Status(reply));
            }
    }

    [Fact]
    public Task Viewer_rule_on_every_read_path() => ViewerRuleOnEveryReadPath(Mem());

    // a block in either direction hides the content both ways, whatever its audience; the author and an admin still see it
    internal static async Task BlocksHideContentBothWays(Func<AppDbContext> open)
    {
        await Seed(open);
        var pub = await AddRating(open, "a", 1, "public", Audience.Public);
        var friends = await AddRating(open, "a", 2, "friends", Audience.Friends);
        var bobs = await AddRating(open, "b", 1, "bob public", Audience.Public);

        async Task Check(string viewer, string author, int id, bool visible)
        {
            Assert.Equal(visible ? 200 : 404, Status(await Call(open, viewer, c => c.One(id))));
            Assert.Equal(visible, Ids(await Call(open, viewer, c => c.ForSeries(1, "newest", null))).Contains(id));
            Assert.Equal(visible, RatingIds(await CallP(open, viewer, p => p.Ratings(author == "a" ? "alice" : "Bob", null))).Contains(id));
        }

        await Block(open, "b", "a"); // Bob blocked alice
        await Check("b", "a", pub, false);     // Bob no longer sees alice's public review
        await Check("b", "a", friends, false); // ... nor the friends-only one, though they were friends
        await Check("a", "b", bobs, false);    // and alice no longer sees Bob's
        await Check("a", "a", pub, true);      // an author always sees their own
        await Check("c", "a", pub, true);      // a stranger is unaffected
        await Check("d", "a", pub, true);      // an admin sees across blocks

        // the other direction: carol is blocked by alice
        await Block(open, "a", "c");
        await Check("c", "a", pub, false);
        Assert.Equal(404, Status(await Call(open, "c", c => c.React(pub, new ReactionRequest("Funny")))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.PostReply(pub, new ReplyRequest("hello", false)))));
    }

    [Fact]
    public Task Blocks_hide_content_both_ways() => BlocksHideContentBothWays(Mem());

    [Fact]
    public async Task Hidden_and_missing_reviews_give_the_identical_404_and_empty_text_is_not_a_review()
    {
        var open = Mem();
        await Seed(open);
        var hidden = await AddRating(open, "a", 1, "secret", Audience.OnlyMe);
        var noText = await AddRating(open, "a", 2, null);

        var a = await Call(open, "c", c => c.One(hidden));
        var b = await Call(open, "c", c => c.One(9999));
        var e = await Call(open, "a", c => c.One(noText)); // even the author: a rating without text is not a review
        Assert.IsType<NotFoundResult>(a);
        Assert.IsType<NotFoundResult>(b);
        Assert.IsType<NotFoundResult>(e);
        Assert.Equal(404, Status(await Call(open, "c", c => c.React(hidden, new ReactionRequest("Helpful")))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.React(9999, new ReactionRequest("Helpful")))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.Replies(hidden, null))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.Replies(9999, null))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.PostReply(hidden, new ReplyRequest("x", false)))));
        Assert.Equal(404, Status(await Call(open, "c", c => c.PostReply(9999, new ReplyRequest("x", false)))));
    }

    [Fact]
    public async Task Profile_ratings_404_is_the_same_for_unknown_and_blocked_by_target()
    {
        var open = Mem();
        await Seed(open);
        await Block(open, "a", "c"); // alice blocked carol

        var blocked = await CallP(open, "c", p => p.Ratings("alice", null));
        var unknown = await CallP(open, "c", p => p.Ratings("nobody", null));
        Assert.Equal(404, Status(blocked));
        Assert.Equal(404, Status(unknown));
        Assert.Equal(unknown.GetType(), blocked.GetType());
        Assert.Equal(404, Status(await CallP(open, null, p => p.Ratings("bad name!", null))));
        Assert.Equal(200, Status(await CallP(open, "b", p => p.Ratings("alice", null))));
        // the viewer blocked the profile owner: the profile still exists but shows nothing
        await AddRating(open, "a", 1, "x");
        await Block(open, "b", "a");
        Assert.Empty(RatingIds(await CallP(open, "b", p => p.Ratings("alice", null))));
    }

    // ---- aggregates ignore audiences; the title page no longer lists reviews ----

    [Fact]
    public async Task Averages_count_every_rating_and_the_title_page_lists_no_review_text()
    {
        var open = Mem();
        await Seed(open);
        await AddRating(open, "a", 1, "private words", Audience.OnlyMe);
        using var db = open();
        var t = db.Titles.Find(1)!;
        t.RatingCount = 1;
        t.RatingSum = 8;
        db.SaveChanges();

        var res = Json(await As(new TitlesController(db), null).Get(1));
        Assert.Equal(1, res.GetProperty("ratingCount").GetInt32());
        Assert.Equal(8, res.GetProperty("averages").GetProperty("overall").GetDouble());
        Assert.False(res.TryGetProperty("reviews", out _));
        Assert.DoesNotContain("private words", res.ToString());
    }

    // ---- review lists: sort and keyset ----

    // 25 public reviews with tied and negative scores; paging through must return each exactly once, in order
    internal static async Task SortAndKeysetAgree(Func<AppDbContext> open)
    {
        await Seed(open);
        using (var db = open())
        {
            for (var i = 0; i < 25; i++) db.Users.Add(new AppUser { Id = $"u{i:00}", UserName = $"user{i}", NormalizedUserName = $"USER{i}", EmailConfirmed = true });
            await db.SaveChangesAsync();
        }
        var made = new List<(int Id, int Score)>();
        for (var i = 0; i < 25; i++)
        {
            var score = i % 5 - 2; // -2..2, many ties
            made.Add((await AddRating(open, $"u{i:00}", 1, "text " + i, score: score), score));
        }
        await AddRating(open, "a", 1, null);                          // no text: never listed
        await AddRating(open, "b", 1, "hidden", Audience.OnlyMe);      // not visible to the anonymous viewer

        async Task<List<int>> Walk(string sort, int expectedPages)
        {
            var got = new List<int>();
            string? cursor = null;
            var pages = 0;
            do
            {
                var page = Json(await Call(open, null, c => c.ForTitle(1, sort, cursor)));
                var ids = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
                Assert.InRange(ids.Count, 1, ReviewRules.PageSize);
                got.AddRange(ids);
                pages++;
                var next = page.GetProperty("nextCursor");
                cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            } while (cursor != null);
            Assert.Equal(expectedPages, pages);
            return got;
        }

        Assert.Equal(made.OrderByDescending(m => m.Score).ThenByDescending(m => m.Id).Select(m => m.Id), await Walk("helpful", 3));
        Assert.Equal(made.OrderByDescending(m => m.Id).Select(m => m.Id), await Walk("newest", 3));
        // no sort given means helpful
        Assert.Equal(Ids(await Call(open, null, c => c.ForTitle(1, null, null))), Ids(await Call(open, null, c => c.ForTitle(1, "helpful", null))));
        // a cursor from the other sort is refused, not misread
        var helpfulCursor = Json(await Call(open, null, c => c.ForTitle(1, "helpful", null))).GetProperty("nextCursor").GetString();
        Assert.Equal(400, Status(await Call(open, null, c => c.ForTitle(1, "newest", helpfulCursor))));
        Assert.Equal(400, Status(await Call(open, null, c => c.ForTitle(1, "helpful", "!!!"))));
        Assert.Equal(404, Status(await Call(open, null, c => c.ForTitle(404, null, null))));
    }

    [Fact]
    public Task Sort_and_keyset_agree() => SortAndKeysetAgree(Mem());

    [Fact]
    public async Task Series_reviews_are_whole_title_only_and_labelled_and_title_reviews_label_the_volume()
    {
        var open = Mem();
        await Seed(open);
        using (var db = open())
        {
            db.Titles.Find(1)!.Volumes = 3;
            await db.SaveChangesAsync();
        }
        var whole = await AddRating(open, "a", 1, "whole");
        var vol = await AddRating(open, "a", 1, "volume two", volume: 2);
        var other = await AddRating(open, "b", 2, "other title");

        var series = Json(await Call(open, null, c => c.ForSeries(1, "newest", null)));
        var items = series.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { other, whole }, items.Select(i => i.GetProperty("id").GetInt32()));
        Assert.Equal(new[] { "Title 2", "Title 1" }, items.Select(i => i.GetProperty("titleName").GetString()));

        var title = Json(await Call(open, null, c => c.ForTitle(1, "newest", null))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { vol, whole }, title.Select(i => i.GetProperty("id").GetInt32()));
        Assert.Equal(2, title[0].GetProperty("volume").GetInt32());
        Assert.Equal(JsonValueKind.Null, title[1].GetProperty("volume").ValueKind);
        Assert.Equal("alice", title[0].GetProperty("userName").GetString());
        Assert.False(title[0].TryGetProperty("userId", out _) || title[0].TryGetProperty("email", out _)); // username only
    }

    // ---- rating form: audience and spoiler ----

    static async Task<IActionResult> Rate(Func<AppDbContext> open, string me, int title, RatingRequest body, int? volume = null)
    {
        using var db = open();
        return await As(new RatingsController(db), me).Put(title, body, volume);
    }

    [Fact]
    public async Task New_ratings_are_public_updates_keep_the_audience_and_bad_values_are_refused()
    {
        var open = Mem();
        await Seed(open);
        var body = new RatingRequest(8, 8, 8, 8, "nice", null, true);

        var first = Json(await Rate(open, "a", 1, body));
        Assert.Equal("Public", first.GetProperty("audience").GetString());
        Assert.True(first.GetProperty("spoiler").GetBoolean());

        var narrowed = Json(await Rate(open, "a", 1, body with { Audience = "onlyme" }));
        Assert.Equal("OnlyMe", narrowed.GetProperty("audience").GetString());
        // omitting the audience on a later save leaves it alone
        Assert.Equal("OnlyMe", Json(await Rate(open, "a", 1, body with { Story = 9 })).GetProperty("audience").GetString());

        foreach (var bad in new[] { "everyone", "1", "", "Friends,Public" })
            Assert.Equal(400, Status(await Rate(open, "a", 1, body with { Audience = bad })));
        Assert.Equal(400, Status(await Rate(open, "a", 1, body with { ReviewText = new string('x', 2001) })));

        // a spoiler flag without text means nothing; the text is never cut on the server
        var plain = Json(await Rate(open, "a", 2, new RatingRequest(5, 5, 5, 5, null, null, true)));
        Assert.False(plain.GetProperty("spoiler").GetBoolean());
        var long2000 = new string('y', 2000);
        await Rate(open, "b", 2, new RatingRequest(5, 5, 5, 5, long2000, null, true));
        var shown = Json(await Call(open, null, c => c.ForTitle(2, null, null))).GetProperty("items")[0];
        Assert.Equal(long2000, shown.GetProperty("reviewText").GetString());
        Assert.True(shown.GetProperty("spoiler").GetBoolean());
    }

    [Fact]
    public async Task Audience_endpoints_change_only_the_callers_own_rows()
    {
        var open = Mem();
        await Seed(open);
        await Rate(open, "a", 1, new RatingRequest(8, 8, 8, 8, "x"));
        using var db = open();
        db.WatchList.Add(new WatchListEntry { UserId = "a", TitleId = 1, Status = WatchStatus.Planning });
        await db.SaveChangesAsync();

        var ratings = As(new RatingsController(db), "b");
        Assert.Equal(404, Status(await ratings.SetAudience(1, new AudienceRequest("OnlyMe")))); // Bob has no rating of title 1
        Assert.Equal(400, Status(await As(new RatingsController(db), "a").SetAudience(1, new AudienceRequest("2"))));
        Assert.Equal(200, Status(await As(new RatingsController(db), "a").SetAudience(1, new AudienceRequest("Friends"))));
        Assert.Equal(Audience.Friends, (await db.Ratings.AsNoTracking().SingleAsync()).Audience);
        Assert.Equal("Friends", Json(await As(new RatingsController(db), "a").Mine()).EnumerateArray().Single().GetProperty("audience").GetString());

        var list = As(new WatchListController(db), "a");
        Assert.Equal(Audience.Public, (await db.WatchList.AsNoTracking().SingleAsync()).Audience); // default for a new entry
        Assert.Equal(404, Status(await As(new WatchListController(db), "b").SetAudience(1, new AudienceRequest("OnlyMe"))));
        Assert.Equal(400, Status(await list.SetAudience(1, new AudienceRequest("nobody"))));
        Assert.Equal(200, Status(await list.SetAudience(1, new AudienceRequest("OnlyMe"))));
        Assert.Equal(Audience.OnlyMe, (await db.WatchList.AsNoTracking().SingleAsync()).Audience);
        // a status update that names no audience keeps it
        Assert.Equal(200, Status(await list.Put(1, new WatchListRequest("Watching", 3))));
        Assert.Equal(Audience.OnlyMe, (await db.WatchList.AsNoTracking().SingleAsync()).Audience);
        // a new entry may choose
        Assert.Equal(200, Status(await list.Put(2, new WatchListRequest("Planning", 0, "Friends"))));
        Assert.Equal(Audience.Friends, (await db.WatchList.AsNoTracking().SingleAsync(w => w.TitleId == 2)).Audience);
        Assert.Equal(400, Status(await list.Put(3, new WatchListRequest("Planning", 0, "all"))));
    }

    // ---- profile ratings ----

    [Fact]
    public async Task Profile_ratings_page_by_twenty_hide_spoiler_snippets_and_cut_long_ones()
    {
        var open = Mem();
        await Seed(open);
        using (var db = open())
        {
            for (var i = 4; i <= 25; i++) db.Titles.Add(new Title { Id = i, AniListId = i, Name = "T" + i, SeriesId = 1, Position = i });
            await db.SaveChangesAsync();
        }
        for (var i = 1; i <= 25; i++)
            await AddRating(open, "a", i, i == 25 ? new string('z', 300) : i == 24 ? "secret ending" : "short", spoiler: i == 24);

        var first = Json(await CallP(open, null, p => p.Ratings("alice", null)));
        var items = first.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(20, items.Count);
        Assert.Equal("T25", items[0].GetProperty("titleName").GetString());
        Assert.Equal("Show", items[0].GetProperty("seriesName").GetString());
        Assert.Equal(143, items[0].GetProperty("snippet").GetString()!.Length); // 140 + "..."
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("snippet").ValueKind); // a spoiler review sends no text
        Assert.True(items[1].GetProperty("spoiler").GetBoolean());
        Assert.True(items[1].GetProperty("hasReview").GetBoolean());
        Assert.DoesNotContain("secret ending", first.ToString());

        var cursor = first.GetProperty("nextCursor").GetInt32();
        var second = Json(await CallP(open, null, p => p.Ratings("alice", cursor)));
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }

    // ---- reactions ----

    static async Task<JsonElement> React(Func<AppDbContext> open, string me, int id, string kind) =>
        Json(await Call(open, me, c => c.React(id, new ReactionRequest(kind))));

    static async Task<(int H, int U, int F, int Score)> Counts(Func<AppDbContext> open, int id)
    {
        using var db = open();
        var r = await db.Ratings.AsNoTracking().SingleAsync(x => x.Id == id);
        return (r.HelpfulCount, r.UnhelpfulCount, r.FunnyCount, r.Score);
    }

    [Fact]
    public async Task Reactions_choose_change_and_clear_with_matching_counters()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");

        var r = await React(open, "b", id, "Helpful");
        Assert.Equal(("Helpful", 1, 0, 0), (r.GetProperty("myReaction").GetString(), r.GetProperty("helpful").GetInt32(), r.GetProperty("unhelpful").GetInt32(), r.GetProperty("funny").GetInt32()));
        await React(open, "c", id, "unhelpful"); // case-insensitive name
        Assert.Equal((1, 1, 0, 0), await Counts(open, id));
        await React(open, "b", id, "Helpful"); // the same again changes nothing
        Assert.Equal((1, 1, 0, 0), await Counts(open, id));

        await React(open, "b", id, "Funny"); // change
        Assert.Equal((0, 1, 1, -1), await Counts(open, id));
        Assert.Equal(2, await Reactions(open));

        var cleared = Json(await Call(open, "b", c => c.Unreact(id)));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("myReaction").ValueKind);
        Assert.Equal((0, 1, 0, -1), await Counts(open, id));
        Assert.Equal(200, Status(await Call(open, "b", c => c.Unreact(id)))); // nothing to clear is fine
        Assert.Equal((0, 1, 0, -1), await Counts(open, id));

        // the list shows the viewer's own reaction and the totals
        var item = Json(await Call(open, "c", c => c.One(id)));
        Assert.Equal("Unhelpful", item.GetProperty("myReaction").GetString());
        Assert.Equal(1, item.GetProperty("unhelpful").GetInt32());

        // bad kinds, numbers and your own review
        foreach (var bad in new[] { "", "Love", "1", "0" })
            Assert.Equal(400, Status(await Call(open, "b", c => c.React(id, new ReactionRequest(bad)))));
        Assert.Equal(400, Status(await Call(open, "b", c => c.React(id, new ReactionRequest(null)))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.React(id, new ReactionRequest("Helpful")))));
    }

    static async Task<int> Reactions(Func<AppDbContext> open)
    {
        using var db = open();
        return await db.Reactions.CountAsync();
    }

    static async Task<List<Notification>> Notes(Func<AppDbContext> open, string user, NotificationType? type = null)
    {
        using var db = open();
        return await db.Notifications.Where(n => n.UserId == user && (type == null || n.Type == type)).OrderBy(n => n.Id).ToListAsync();
    }

    [Fact]
    public async Task Reaction_notifies_once_per_reactor_and_cleans_up_when_cleared()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");

        await React(open, "b", id, "Helpful");
        var n = Assert.Single(await Notes(open, "a"));
        Assert.Equal((NotificationType.Reaction, "b", id), (n.Type, n.ActorId, n.SubjectId));

        await React(open, "b", id, "Funny");    // a change: no second notification
        await React(open, "c", id, "Funny");    // another reactor: one more
        Assert.Equal(new[] { "b", "c" }, (await Notes(open, "a")).Select(x => x.ActorId));

        await Call(open, "b", c => c.Unreact(id));
        Assert.Equal(new[] { "c" }, (await Notes(open, "a")).Select(x => x.ActorId)); // gone with the reaction
        await React(open, "b", id, "Helpful");   // reacting again after clearing is a first reaction again
        Assert.Equal(2, (await Notes(open, "a")).Count);
        Assert.Empty(await Notes(open, "b"));    // the reactor is told nothing
    }

    [Fact]
    public async Task Reaction_notifications_respect_preferences_and_blocks()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        await Do(open, db => db.NotificationPreferences.Add(new NotificationPreference { UserId = "a", Type = NotificationType.Reaction, Enabled = false }));

        await React(open, "b", id, "Helpful");
        Assert.Empty(await Notes(open, "a")); // turned off: the reaction still counts
        Assert.Equal((1, 0, 0, 1), await Counts(open, id));

        // an admin sees across a block, but a blocked pair is never notified
        await Do(open, db => db.NotificationPreferences.RemoveRange(db.NotificationPreferences));
        await Block(open, "a", "d");
        await React(open, "d", id, "Helpful");
        Assert.Empty(await Notes(open, "a"));
        Assert.Equal(2, (await Counts(open, id)).H);
    }

    // ---- replies ----

    static async Task<IActionResult> Reply(Func<AppDbContext> open, string me, int review, string? body, bool spoiler = false) =>
        await Call(open, me, c => c.PostReply(review, new ReplyRequest(body, spoiler)));

    [Fact]
    public async Task Reply_rules_trim_limit_and_need_a_verified_email()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        await Do(open, db => db.Users.Add(new AppUser { Id = "u", UserName = "unverified", NormalizedUserName = "UNVERIFIED" }));

        var ok = Json(await Reply(open, "b", id, "  well said  ", true));
        Assert.Equal("well said", ok.GetProperty("body").GetString());
        Assert.True(ok.GetProperty("spoiler").GetBoolean());
        Assert.Equal("Bob", ok.GetProperty("userName").GetString());
        Assert.Equal(201, Status(await Reply(open, "b", id, new string('x', 1000))));
        foreach (var bad in new[] { null, "", "   \n ", new string('x', 1001), " " + new string('x', 1001) })
            Assert.Equal(400, Status(await Reply(open, "b", id, bad)));
        Assert.Equal(403, Status(await Reply(open, "u", id, "hi")));
        Assert.Equal(2, await RepliesCount(open));
    }

    static async Task<int> RepliesCount(Func<AppDbContext> open)
    {
        using var db = open();
        return await db.Replies.CountAsync();
    }

    [Fact]
    public async Task Replies_list_oldest_first_by_keyset_and_blocked_pairs_do_not_see_each_other()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        for (var i = 0; i < 12; i++) await Reply(open, i % 2 == 0 ? "b" : "c", id, "reply " + i);

        var one = Json(await Call(open, null, c => c.Replies(id, null)));
        var firstIds = one.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(10, firstIds.Count);
        Assert.Equal(firstIds.OrderBy(x => x), firstIds); // oldest first
        var two = Json(await Call(open, null, c => c.Replies(id, one.GetProperty("nextCursor").GetInt32())));
        Assert.Equal(2, two.GetProperty("items").GetArrayLength());
        Assert.True(two.GetProperty("items")[0].GetProperty("id").GetInt32() > firstIds[^1]);
        Assert.Equal(JsonValueKind.Null, two.GetProperty("nextCursor").ValueKind);

        // reply counts on the review follow the viewer: carol blocked Bob, so she sees only her own six
        Assert.Equal(12, Json(await Call(open, null, c => c.One(id))).GetProperty("replyCount").GetInt32());
        await Block(open, "c", "b");
        Assert.Equal(6, Json(await Call(open, "c", c => c.One(id))).GetProperty("replyCount").GetInt32());
        Assert.Equal(6, Json(await Call(open, "c", c => c.Replies(id, null))).GetProperty("items").GetArrayLength());
        Assert.Equal(6, Json(await Call(open, "b", c => c.Replies(id, null))).GetProperty("items").GetArrayLength());
        Assert.Equal(12, Json(await Call(open, "d", c => c.One(id))).GetProperty("replyCount").GetInt32()); // admin sees all
    }

    [Fact]
    public async Task Replies_tell_who_may_delete_them_and_only_those_people_can()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        var byBob = Json(await Reply(open, "b", id, "from bob")).GetProperty("id").GetInt32();
        var byCarol = Json(await Reply(open, "c", id, "from carol")).GetProperty("id").GetInt32();

        async Task<bool> CanDelete(string? viewer, int reply) =>
            Json(await Call(open, viewer, c => c.Replies(id, null))).GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetInt32() == reply).GetProperty("canDelete").GetBoolean();
        Assert.True(await CanDelete("b", byBob));
        Assert.False(await CanDelete("b", byCarol));
        Assert.True(await CanDelete("a", byBob));   // the review's author
        Assert.False(await CanDelete(null, byBob));
        Assert.True(await CanDelete("d", byBob));   // admin, through the admin route

        async Task<int> Delete(string me, int reply)
        {
            using var db = open();
            return Status(await As(new ReviewsController(db), me).DeleteReply(reply));
        }
        Assert.Equal(404, await Delete("c", byBob));   // a stranger gets a plain 404 and the reply stays
        Assert.Equal(404, await Delete("d", byBob));   // admins use /admin/replies
        Assert.Equal(2, await RepliesCount(open));
        Assert.Equal(204, await Delete("b", byBob));   // its author
        Assert.Equal(204, await Delete("a", byCarol)); // the review's author
        Assert.Equal(404, await Delete("a", byCarol)); // already gone
        Assert.Equal(0, await RepliesCount(open));
    }

    [Fact]
    public async Task Review_author_cannot_delete_replies_across_a_block()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        var reply = Json(await Reply(open, "c", id, "hi")).GetProperty("id").GetInt32();
        await Block(open, "a", "c");

        using var db = open();
        Assert.Equal(404, Status(await As(new ReviewsController(db), "a").DeleteReply(reply)));
        Assert.Equal(204, Status(await As(new ReviewsController(db), "c").DeleteReply(reply))); // the replier may still remove their own
    }

    [Fact]
    public async Task Reply_notifications_skip_self_respect_preferences_and_clean_up()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");

        await Reply(open, "a", id, "my own thread");
        Assert.Empty(await Notes(open, "a")); // no self-notification

        var replyId = Json(await Reply(open, "b", id, "hello")).GetProperty("id").GetInt32();
        var n = Assert.Single(await Notes(open, "a"));
        Assert.Equal((NotificationType.Reply, "b", replyId), (n.Type, n.ActorId, n.SubjectId));

        // the bell links to the review
        using (var db = open())
        {
            var item = Json(await Bell(db, "a").List(null)).GetProperty("items")[0];
            Assert.Equal("Reply", item.GetProperty("type").GetString());
            Assert.Equal(id, item.GetProperty("reviewId").GetInt32());
        }

        await Do(open, db => db.NotificationPreferences.Add(new NotificationPreference { UserId = "a", Type = NotificationType.Reply, Enabled = false }));
        await Reply(open, "c", id, "quiet");
        Assert.Single(await Notes(open, "a")); // still just Bob's

        using (var db = open()) Assert.Equal(204, Status(await As(new ReviewsController(db), "b").DeleteReply(replyId)));
        Assert.Empty(await Notes(open, "a")); // deleting the reply removes its notification
    }

    [Fact]
    public async Task Reply_notification_is_skipped_across_a_block_an_admin_can_see()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "review");
        await Block(open, "a", "d");
        Assert.Equal(201, Status(await Reply(open, "d", id, "admin reply")));
        Assert.Empty(await Notes(open, "a"));
    }

    [Fact]
    public async Task Blocking_removes_reply_and_reaction_notifications_between_the_pair()
    {
        var open = Mem();
        await Seed(open);
        var mine = await AddRating(open, "a", 1, "review");
        var theirs = await AddRating(open, "b", 1, "bobs review");
        await Reply(open, "b", mine, "hi");
        await React(open, "b", mine, "Helpful");
        await React(open, "a", theirs, "Helpful");
        await Reply(open, "c", mine, "from carol");
        Assert.Equal(3, (await Notes(open, "a")).Count);

        using (var db = open()) Assert.Equal(204, Status(await Friends(db, "a").Block("bob")));

        Assert.Equal(new[] { "c" }, (await Notes(open, "a")).Select(n => n.ActorId));
        Assert.Empty(await Notes(open, "b"));
    }

    [Fact]
    public async Task Deleting_a_rating_or_its_text_removes_notifications_about_it()
    {
        var open = Mem();
        await Seed(open);
        await Rate(open, "a", 1, new RatingRequest(8, 8, 8, 8, "first draft"));
        var id = await Do2(open, db => db.Ratings.Single().Id);
        await Reply(open, "b", id, "hello");
        await React(open, "c", id, "Funny");
        Assert.Equal(2, (await Notes(open, "a")).Count);

        await Rate(open, "a", 1, new RatingRequest(8, 8, 8, 8, "edited text")); // an edit keeps them
        Assert.Equal(2, (await Notes(open, "a")).Count);
        await Rate(open, "a", 1, new RatingRequest(8, 8, 8, 8, "  ")); // clearing the text removes them
        Assert.Empty(await Notes(open, "a"));

        await Rate(open, "a", 1, new RatingRequest(8, 8, 8, 8, "again"));
        await React(open, "b", id, "Helpful"); // a first reaction by Bob
        Assert.Single(await Notes(open, "a"));
        using (var db = open()) Assert.IsType<NoContentResult>(await As(new RatingsController(db), "a").Delete(1));
        Assert.Empty(await Notes(open, "a"));
    }

    static async Task<T> Do2<T>(Func<AppDbContext> open, Func<AppDbContext, T> read)
    {
        using var db = open();
        return await Task.FromResult(read(db));
    }

    // ---- admin ----

    [Fact]
    public async Task Admin_removal_clears_the_text_keeps_the_score_and_the_routes_are_admin_only()
    {
        var open = Mem();
        await Seed(open);
        var id = await AddRating(open, "a", 1, "rude words", spoiler: true);
        await Reply(open, "b", id, "hello");
        var reply = await Do2(open, db => db.Replies.Single().Id);

        // the class carries [AdminOnly], whose filter re-checks the role on every request (see VerificationTests)
        Assert.NotEmpty(typeof(AdminReviewsController).GetCustomAttributes(typeof(AdminOnlyAttribute), true));
        Assert.NotEmpty(typeof(AdminReviewsController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));

        // a normal user's route to the same reply does not work for anyone but its authors
        using (var db = open()) Assert.Equal(404, Status(await As(new ReviewsController(db), "c").DeleteReply(reply)));

        using (var db = open())
        {
            var admin = As(new AdminReviewsController(db), "d");
            Assert.Equal(204, Status(await admin.RemoveText(id)));
            Assert.Equal(404, Status(await admin.RemoveText(id))); // already empty
            Assert.Equal(404, Status(await admin.RemoveText(9999)));
        }
        using (var db = open())
        {
            var r = await db.Ratings.AsNoTracking().SingleAsync();
            Assert.Null(r.ReviewText);
            Assert.False(r.Spoiler);
            Assert.Equal(8, r.Overall); // the score stays
            Assert.Empty(db.Notifications.Where(n => n.UserId == "a"));
        }
        Assert.Equal(404, Status(await Call(open, null, c => c.One(id)))); // no longer a review
        Assert.Empty(Ids(await Call(open, null, c => c.ForTitle(1, null, null))));

        using (var db = open())
        {
            var admin = As(new AdminReviewsController(db), "d");
            Assert.Equal(204, Status(await admin.DeleteReply(reply)));
            Assert.Equal(404, Status(await admin.DeleteReply(reply)));
        }
        Assert.Equal(0, await RepliesCount(open));
    }

    [Fact]
    public async Task Admin_role_is_read_from_the_database_each_time()
    {
        var open = Mem();
        await Seed(open);
        using var db = open();
        Assert.True(await ReviewRules.IsAdmin(db, "d"));
        Assert.False(await ReviewRules.IsAdmin(db, "a"));
        Assert.False(await ReviewRules.IsAdmin(db, null));
        db.UserRoles.RemoveRange(db.UserRoles);
        await db.SaveChangesAsync();
        Assert.False(await ReviewRules.IsAdmin(db, "d")); // demoted: no admin view at once
    }

    // ---- notification preferences list the new types ----

    [Fact]
    public async Task Preferences_list_reply_and_reaction_enabled_by_default()
    {
        using var db = await Db();
        var list = Json(await Bell(db, "a").Preferences()).EnumerateArray().ToDictionary(p => p.GetProperty("type").GetString()!, p => p.GetProperty("enabled").GetBoolean());
        Assert.True(list["Reply"]);
        Assert.True(list["Reaction"]);
        Assert.Equal(200, Status(await Bell(db, "a").SetPreference("reaction", new PreferenceRequest(false))));
    }
}
