using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// slur censor on write, reports, the admin queue and the friends' feed. Scenarios take a context factory so ReportPostgresTests
// runs them on the real database. Users: a alice (author), b Bob (a's friend), c carol (stranger), d dave (admin).
public class ReportTests
{
    static async Task<IActionResult> Run<T>(Func<AppDbContext> open, string me, Func<T, Task<IActionResult>> f, Func<AppDbContext, T> make) where T : ControllerBase
    {
        using var db = open();
        return await f(As(make(db), me));
    }

    static Task<IActionResult> Reports(Func<AppDbContext> open, string me, Func<ReportsController, Task<IActionResult>> f) => Run(open, me, f, d => new ReportsController(d));
    static Task<IActionResult> Queue(Func<AppDbContext> open, Func<AdminReportsController, Task<IActionResult>> f) => Run(open, "d", f, d => new AdminReportsController(d));
    static Task<IActionResult> Feed(Func<AppDbContext> open, string me, int? cursor = null) => Run(open, me, (FeedController c) => c.Get(cursor), d => new FeedController(d));
    static Task<IActionResult> Bell(Func<AppDbContext> open, string me) => Run(open, me, (NotificationsController c) => c.List(null), d => new NotificationsController(d));
    static ReportRequest Spam => new("Spam", null);

    static async Task<int> Reply(Func<AppDbContext> open, string me, int review, string body) =>
        Json(await Call(open, me, c => c.PostReply(review, new ReplyRequest(body, false)))).GetProperty("id").GetInt32();

    static async Task Do(Func<AppDbContext> open, Action<AppDbContext> change)
    {
        using var db = open();
        change(db);
        await db.SaveChangesAsync();
    }

    static async Task<List<Report>> AllReports(Func<AppDbContext> open)
    {
        using var db = open();
        return await db.Reports.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    // ---- censor on write ----

    [Fact]
    public async Task Review_and_reply_text_are_stored_censored()
    {
        var open = Mem();
        await Seed(open);
        using (var db = open())
            await As(new RatingsController(db), "a").Put(1, new RatingRequest(8, 8, 8, 8, "  great, but the fans are faggots  "));
        using (var db = open())
            Assert.Equal("great, but the fans are *******", (await db.Ratings.SingleAsync()).ReviewText);

        var id = await Reply(open, "b", 1, "what a n1gger");
        using (var db = open())
            Assert.Equal("what a ******", (await db.Replies.SingleAsync(r => r.Id == id)).Body);
    }

    // ---- filing reports ----

    internal static async Task FilingFollowsTheViewerRuleAndTheLimits(Func<AppDbContext> open)
    {
        await Seed(open);
        var pub = await AddRating(open, "a", 1, "public review");
        var friends = await AddRating(open, "a", 2, "friends review", Audience.Friends);
        var onlyMe = await AddRating(open, "a", 3, "private review", Audience.OnlyMe);
        var reply = await Reply(open, "b", pub, "bob reply");

        // visibility: carol (stranger) reaches only the public one; hidden and missing are the same 404
        Assert.Equal(201, Status(await Reports(open, "c", r => r.ReportReview(pub, new ReportRequest("harassment", " rude ")))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReview(friends, Spam))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReview(onlyMe, Spam))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReview(9999, Spam))));
        Assert.Equal(201, Status(await Reports(open, "b", r => r.ReportReview(friends, Spam)))); // a friend may
        Assert.Equal(404, Status(await Reports(open, "b", r => r.ReportReview(onlyMe, Spam))));
        var one = (await AllReports(open)).First(x => x.TargetId == pub);
        Assert.Equal(("c", "a", "public review", ReportReason.Harassment, "rude"), (one.ReporterId, one.TargetUserId, one.Snapshot, one.Reason, one.Note));

        // one open report per target; own content; reasons; note length; verified email
        Assert.Equal(409, Status(await Reports(open, "c", r => r.ReportReview(pub, new ReportRequest("Hate", null)))));
        Assert.Equal(400, Status(await Reports(open, "a", r => r.ReportReview(pub, Spam))));
        Assert.Equal(400, Status(await Reports(open, "b", r => r.ReportReply(reply, Spam))));
        Assert.Equal(400, Status(await Reports(open, "b", r => r.ReportReview(pub, new ReportRequest("Rude", null)))));
        Assert.Equal(400, Status(await Reports(open, "b", r => r.ReportReview(pub, new ReportRequest("1", null)))));
        Assert.Equal(400, Status(await Reports(open, "b", r => r.ReportReview(pub, new ReportRequest("Spam", new string('x', 501))))));
        await Do(open, db => db.Users.Find("b")!.EmailConfirmed = false);
        Assert.Equal(403, Status(await Reports(open, "b", r => r.ReportReview(pub, Spam))));
        await Do(open, db => db.Users.Find("b")!.EmailConfirmed = true);

        // a reply follows its review's audience and the block rules
        Assert.Equal(201, Status(await Reports(open, "c", r => r.ReportReply(reply, Spam))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReply(9999, Spam))));
        var hiddenReply = await Reply(open, "a", friends, "inside friends");
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReply(hiddenReply, Spam))));
        await Do(open, db => db.UserBlocks.Add(new UserBlock { BlockerId = "b", BlockedId = "c" }));
        var other = await Reply(open, "b", pub, "bob again");
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReply(other, Spam)))); // blocked by the author
        var carols = await AddRating(open, "c", 2, "carol", Audience.Public);
        Assert.Equal(404, Status(await Reports(open, "b", r => r.ReportReview(carols, Spam)))); // and the other way

        // profiles: not yourself, not a user who blocked you, not an unknown one
        Assert.Equal(201, Status(await Reports(open, "c", r => r.ReportProfile("alice", new ReportRequest("Other", "bio")))));
        Assert.Equal(409, Status(await Reports(open, "c", r => r.ReportProfile("ALICE", Spam))));
        Assert.Equal(400, Status(await Reports(open, "a", r => r.ReportProfile("alice", Spam))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportProfile("Bob", Spam)))); // Bob blocked carol
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportProfile("nobody", Spam))));
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportProfile("bad name!", Spam))));
    }

    [Fact]
    public Task Filing_follows_the_viewer_rule_and_the_limits() => FilingFollowsTheViewerRuleAndTheLimits(Mem());

    // ---- the admin queue ----

    internal static async Task QueueResolvesAndTellsTheReporters(Func<AppDbContext> open)
    {
        await Seed(open);
        var review = await AddRating(open, "a", 1, "bad review");
        var reply = await Reply(open, "b", review, "bad reply");
        await Reports(open, "c", r => r.ReportReview(review, Spam));
        await Reports(open, "b", r => r.ReportReview(review, new ReportRequest("Hate", null)));
        await Reports(open, "c", r => r.ReportReply(reply, Spam));
        await Reports(open, "c", r => r.ReportProfile("alice", Spam));
        await Call(open, "c", c => c.React(review, new ReactionRequest("Helpful"))); // a notification that must go with the text
        var ids = (await AllReports(open)).Select(r => r.Id).ToList();
        Assert.Equal(4, ids.Count);

        // oldest first, with the snapshot and names
        var items = Json(await Queue(open, q => q.List(null))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(ids, items.Select(i => i.GetProperty("id").GetInt32()).ToList());
        Assert.Equal(("Review", "bad review", "carol", "alice"), (items[0].GetProperty("target").GetString(), items[0].GetProperty("snapshot").GetString(),
            items[0].GetProperty("reporter").GetString(), items[0].GetProperty("targetUser").GetString()));

        // a profile has nothing to remove; dismissing resolves it and leaves everything else; a second try is a 404
        Assert.Equal(400, Status(await Queue(open, q => q.Remove(ids[3]))));
        Assert.Equal(204, Status(await Queue(open, q => q.Dismiss(ids[3]))));
        Assert.Equal(404, Status(await Queue(open, q => q.Dismiss(ids[3]))));
        Assert.Equal(404, Status(await Queue(open, q => q.Remove(ids[3]))));

        // removing a reply deletes it
        Assert.Equal(204, Status(await Queue(open, q => q.Remove(ids[2]))));
        using (var db = open()) Assert.Empty(db.Replies);

        // removing a review strips its text and settles both reports on it
        Assert.Equal(204, Status(await Queue(open, q => q.Remove(ids[0]))));
        using (var db = open())
        {
            Assert.Null((await db.Ratings.SingleAsync()).ReviewText);
            Assert.DoesNotContain(db.Notifications, n => n.Type != NotificationType.ReportOutcome); // the reaction and reply notices went with it
        }
        Assert.Equal(new[] { ReportStatus.Removed, ReportStatus.Removed, ReportStatus.Removed, ReportStatus.Dismissed }, (await AllReports(open)).Select(r => r.Status).ToArray());
        Assert.All(await AllReports(open), r => Assert.NotNull(r.ResolvedAt));
        Assert.Empty(Json(await Queue(open, q => q.List(null))).GetProperty("items").EnumerateArray());

        // the reporters hear the outcome: carol three times, Bob once
        using (var db = open())
        {
            Assert.Equal(3, await db.Notifications.CountAsync(n => n.UserId == "c" && n.Type == NotificationType.ReportOutcome && n.SubjectId != null));
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == "b" && n.Type == NotificationType.ReportOutcome));
        }
        var bell = Json(await Bell(open, "c")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("outcome").GetString()).ToList();
        Assert.Equal(new[] { "Removed", "Removed", "Dismissed" }, bell); // newest first: ids[0] removed, ids[2] removed, ids[3] dismissed

        // a resolved report no longer blocks a new one
        Assert.Equal(404, Status(await Reports(open, "c", r => r.ReportReview(review, Spam)))); // the text is gone, so there is no review to report
        Assert.Equal(201, Status(await Reports(open, "c", r => r.ReportProfile("alice", Spam))));
    }

    [Fact]
    public Task Queue_dismiss_remove_and_notify() => QueueResolvesAndTellsTheReporters(Mem());

    internal static async Task QueuePagesByKeyset(Func<AppDbContext> open)
    {
        await Seed(open);
        await Do(open, db =>
        {
            for (var i = 1; i <= 25; i++) db.Reports.Add(new Report { ReporterId = "c", Target = ReportTarget.Review, TargetId = i, TargetUserId = "a", Reason = ReportReason.Spam, Snapshot = "t" + i });
        });
        var first = Json(await Queue(open, q => q.List(null)));
        var cursor = first.GetProperty("nextCursor").GetInt32();
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        var second = Json(await Queue(open, q => q.List(cursor)));
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var all = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(25, all.Distinct().Count());
        Assert.Equal(all.OrderBy(x => x), all);
    }

    [Fact]
    public Task Queue_pages_by_keyset() => QueuePagesByKeyset(Mem());

    [Fact]
    public async Task Outcome_notification_respects_the_reporters_preferences()
    {
        var open = Mem();
        await Seed(open);
        var review = await AddRating(open, "a", 1, "text");
        await Do(open, db => db.NotificationPreferences.Add(new NotificationPreference { UserId = "c", Type = NotificationType.ReportOutcome, Enabled = false }));
        await Reports(open, "c", r => r.ReportReview(review, Spam));
        var id = (await AllReports(open)).Single().Id;

        Assert.Equal(204, Status(await Queue(open, q => q.Dismiss(id))));

        Assert.Equal(ReportStatus.Dismissed, (await AllReports(open)).Single().Status);
        using var db = open();
        Assert.Empty(db.Notifications);
    }

    // ---- the friends' feed ----

    internal static async Task FeedShowsFriendsOnlyAndKeepsAudiencesAndBlocks(Func<AppDbContext> open)
    {
        await Seed(open); // a and b are friends
        var pub = await AddRating(open, "a", 1, "public review");
        var friends = await AddRating(open, "a", 2, "friends review", Audience.Friends);
        var onlyMe = await AddRating(open, "a", 3, "private", Audience.OnlyMe);
        var plain = await AddRating(open, "a", 1, null, volume: 1);
        var spoiler = await AddRating(open, "a", 1, "secret ending", volume: 2, spoiler: true);
        var carols = await AddRating(open, "c", 1, "stranger public");
        var bobs = await AddRating(open, "b", 1, "bob own");

        static List<int> Ids(IActionResult r) => Json(r).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("ratingId").GetInt32()).ToList();

        // Bob sees alice's public and friends-only ratings, newest first, and never Only me, strangers or himself
        var bob = await Feed(open, "b");
        Assert.Equal(new[] { spoiler, plain, friends, pub }, Ids(bob));
        var items = Json(bob).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, items[0].GetProperty("snippet").ValueKind); // a spoiler sends no snippet
        Assert.False(items[1].GetProperty("hasReview").GetBoolean());
        Assert.Equal("friends review", items[2].GetProperty("snippet").GetString());
        Assert.DoesNotContain(onlyMe, Ids(bob));

        // alice sees Bob's; carol has no friends so her feed is empty, and an admin gets no wider view
        Assert.Equal(new[] { bobs }, Ids(await Feed(open, "a")));
        Assert.Empty(Ids(await Feed(open, "c")));
        Assert.Empty(Ids(await Feed(open, "d")));

        // a block hides both ways even while the friendship row is still there
        await Do(open, db => db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "b" }));
        Assert.Empty(Ids(await Feed(open, "b")));
        Assert.Empty(Ids(await Feed(open, "a")));
        await Do(open, db => db.UserBlocks.RemoveRange(db.UserBlocks));

        // a pending request is not a friendship; unfriending empties the feed
        await Do(open, db => db.Friendships.Add(new Friendship { UserLowId = "b", UserHighId = "c", RequesterId = "c", Status = FriendStatus.Pending }));
        Assert.Empty(Ids(await Feed(open, "c")));
        Assert.DoesNotContain(carols, Ids(await Feed(open, "b")));
        await Do(open, db => db.Friendships.RemoveRange(db.Friendships.Where(f => f.UserLowId == "a")));
        Assert.Empty(Ids(await Feed(open, "b")));
    }

    [Fact]
    public Task Feed_shows_friends_only_and_keeps_audiences_and_blocks() => FeedShowsFriendsOnlyAndKeepsAudiencesAndBlocks(Mem());

    internal static async Task FeedPagesByKeyset(Func<AppDbContext> open)
    {
        await Seed(open);
        for (var v = 1; v <= 25; v++) await AddRating(open, "a", 1, null, volume: v);

        var first = Json(await Feed(open, "b"));
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        var second = Json(await Feed(open, "b", first.GetProperty("nextCursor").GetInt32()));
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("ratingId").GetInt32()).ToList();
        Assert.Equal(25, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(x => x), ids);
    }

    [Fact]
    public Task Feed_pages_by_keyset() => FeedPagesByKeyset(Mem());
}
