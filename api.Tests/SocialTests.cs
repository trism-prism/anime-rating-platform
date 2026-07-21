using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class SocialTests
{
    // ids sort a < b < c, so "a" is always the low side of a pair with b
    internal static AppUser User(string id, string name) =>
        new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com" };

    internal static async Task<AppDbContext> Db(AppDbContext? db = null)
    {
        db ??= new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.AddRange(User("a", "alice"), User("b", "Bob"), User("c", "carol"));
        await db.SaveChangesAsync();
        return db;
    }

    internal static T As<T>(T controller, string? userId) where T : ControllerBase
    {
        var identity = userId == null ? new ClaimsIdentity() : new ClaimsIdentity(new[] { new Claim("sub", userId) }, "test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    internal static FriendsController Friends(AppDbContext db, string me) => As(new FriendsController(db), me);
    internal static NotificationsController Bell(AppDbContext db, string me) => As(new NotificationsController(db), me);
    internal static ProfilesController Profiles(AppDbContext db, string? me) => As(new ProfilesController(db), me);

    internal static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode!.Value;

    // the controllers' anonymous results as JSON, camelCased the way the API sends them
    internal static JsonElement Json(IActionResult r) =>
        JsonSerializer.SerializeToElement(((ObjectResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    static Task<IActionResult> Send(AppDbContext db, string me, string? username) => Friends(db, me).Send(new SendFriendRequest(username));

    [Fact]
    public async Task Request_then_accept_makes_friends_and_notifies_each_side_once()
    {
        using var db = await Db();

        var sent = Json(await Send(db, "a", "bob")); // case-insensitive
        Assert.Equal("OutgoingPending", sent.GetProperty("state").GetString());
        var f = await db.Friendships.SingleAsync();
        Assert.Equal(("a", "b", "a"), (f.UserLowId, f.UserHighId, f.RequesterId));
        var toBob = await db.Notifications.SingleAsync();
        Assert.Equal(("b", NotificationType.FriendRequest, "a", f.Id), (toBob.UserId, toBob.Type, toBob.ActorId, toBob.SubjectId));

        Assert.Equal(204, Status(await Friends(db, "b").Accept(f.Id)));

        Assert.Equal(FriendStatus.Accepted, (await db.Friendships.SingleAsync()).Status);
        var all = await db.Notifications.OrderBy(n => n.Id).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(("a", NotificationType.FriendAccepted, "b"), (all[1].UserId, all[1].Type, all[1].ActorId)); // the requester hears about it
        Assert.NotNull(all[0].ReadAt); // Bob's copy of the request is read once he answered it
    }

    [Fact]
    public async Task Mutual_requests_accept_instead_of_duplicating()
    {
        using var db = await Db();
        await Send(db, "a", "Bob");

        var back = Json(await Send(db, "b", "alice"));

        Assert.Equal("Friends", back.GetProperty("state").GetString());
        Assert.Equal(FriendStatus.Accepted, (await db.Friendships.SingleAsync()).Status);
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.UserId == "a" && n.Type == NotificationType.FriendAccepted);
    }

    [Fact]
    public async Task Bad_requests_get_clear_errors_and_blockers_stay_invisible()
    {
        using var db = await Db();
        Assert.Equal(400, Status(await Send(db, "a", "alice"))); // myself
        Assert.Equal(404, Status(await Send(db, "a", "nobody")));
        Assert.Equal(404, Status(await Send(db, "a", "")));
        Assert.Equal(404, Status(await Send(db, "a", null)));
        Assert.Equal(404, Status(await Send(db, "a", "bob'; drop table")));

        Assert.Equal(200, Status(await Send(db, "a", "bob")));
        Assert.Equal(409, Status(await Send(db, "a", "bob"))); // already sent
        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);
        Assert.Equal(409, Status(await Send(db, "a", "bob"))); // already friends
        Assert.Equal(409, Status(await Send(db, "b", "alice")));
        Assert.Single(db.Friendships);

        // carol blocks alice: alice's request looks exactly like an unknown name
        await Friends(db, "c").Block("alice");
        var blocked = await Send(db, "a", "carol");
        var unknown = await Send(db, "a", "nobody");
        Assert.Equal(404, Status(blocked));
        Assert.Equal(Json(unknown).ToString(), Json(blocked).ToString());
        // blocking someone who blocked you always works, so you can pre-empt them lifting theirs
        Assert.Equal(204, Status(await Friends(db, "a").Block("carol")));
        await Friends(db, "c").Unblock("alice");
        Assert.Equal(409, Status(await Send(db, "a", "carol"))); // alice blocks carol: refused with a hint
    }

    [Fact]
    public async Task Only_the_right_person_can_answer_a_request()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        var id = (await db.Friendships.SingleAsync()).Id;

        // the sender can't accept or decline their own request; a third party can't touch it at all; the addressee can't cancel
        Assert.Equal(404, Status(await Friends(db, "a").Accept(id)));
        Assert.Equal(404, Status(await Friends(db, "a").Decline(id)));
        Assert.Equal(404, Status(await Friends(db, "c").Accept(id)));
        Assert.Equal(404, Status(await Friends(db, "c").Decline(id)));
        Assert.Equal(404, Status(await Friends(db, "c").Cancel(id)));
        Assert.Equal(404, Status(await Friends(db, "b").Cancel(id)));
        Assert.Equal(404, Status(await Friends(db, "b").Accept(id + 100)));
        Assert.Equal(FriendStatus.Pending, (await db.Friendships.SingleAsync()).Status);

        await Friends(db, "b").Accept(id);
        // once accepted it can't be declined, cancelled or accepted again
        Assert.Equal(404, Status(await Friends(db, "b").Decline(id)));
        Assert.Equal(404, Status(await Friends(db, "a").Cancel(id)));
        Assert.Equal(404, Status(await Friends(db, "b").Accept(id)));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.FriendAccepted)); // no second notice
    }

    [Fact]
    public async Task Decline_is_silent_clears_the_notification_and_allows_asking_again()
    {
        using var db = await Db();
        await Send(db, "a", "bob");

        Assert.Equal(204, Status(await Friends(db, "b").Decline((await db.Friendships.SingleAsync()).Id)));

        Assert.Empty(db.Friendships);
        Assert.Empty(db.Notifications); // Bob's request is gone and Alice got nothing
        Assert.Equal(200, Status(await Send(db, "a", "bob")));
        Assert.Single(db.Friendships);
    }

    [Fact]
    public async Task Cancel_removes_the_request_and_its_notification()
    {
        using var db = await Db();
        await Send(db, "a", "bob");

        Assert.Equal(204, Status(await Friends(db, "a").Cancel((await db.Friendships.SingleAsync()).Id)));

        Assert.Empty(db.Friendships);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Unfriend_removes_the_row_and_both_notifications()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);

        Assert.Equal(204, Status(await Friends(db, "c").Unfriend("alice")));
        Assert.Equal(204, Status(await Friends(db, "a").Unfriend("bob")));

        Assert.Empty(db.Friendships);
        Assert.Empty(db.Notifications);
        Assert.Equal(204, Status(await Friends(db, "a").Unfriend("bob")));
    }

    [Fact]
    public async Task Block_ends_the_friendship_hides_the_profile_and_stops_requests()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);
        await Send(db, "a", "carol"); // a pending request in the same breath

        Assert.Equal(204, Status(await Friends(db, "b").Block("alice")));
        Assert.Equal(204, Status(await Friends(db, "b").Block("alice"))); // again: nothing changes
        Assert.Single(db.UserBlocks);

        Assert.Equal(1, await db.Friendships.CountAsync()); // only the alice-carol request is left
        Assert.DoesNotContain(await db.Notifications.ToListAsync(), n => n.UserId == "a" || (n.UserId == "b" && n.ActorId == "a"));
        Assert.Equal(404, Status(await Profiles(db, "a").Get("bob"))); // alice sees nothing of bob
        Assert.Equal(404, Status(await Send(db, "a", "bob")));
        Assert.Equal("BlockedByViewer", Json(await Profiles(db, "b").Get("alice")).GetProperty("viewerState").GetString());
        Assert.Equal(200, Status(await Profiles(db, "c").Get("bob"))); // everyone else still sees bob

        // block carol while her request to a is pending: it goes with its notification
        await Friends(db, "a").Block("carol");
        Assert.Empty(db.Friendships);
        Assert.Empty(db.Notifications.Where(n => n.SubjectId != null && n.Type == NotificationType.FriendRequest));

        Assert.Equal(204, Status(await Friends(db, "b").Unblock("alice")));
        Assert.Equal(204, Status(await Friends(db, "b").Unblock("alice")));
        Assert.Equal(200, Status(await Profiles(db, "a").Get("bob")));
        var before = await db.UserBlocks.CountAsync();
        Assert.Equal(204, Status(await Friends(db, "a").Block("alice"))); // yourself: a no-op, no row
        Assert.Equal(before, await db.UserBlocks.CountAsync());
        Assert.Equal(404, Status(await Friends(db, "a").Block("nobody")));
    }

    [Fact]
    public async Task Mutual_blocks_are_allowed_and_the_first_blocker_still_decides()
    {
        using var db = await Db();
        await Friends(db, "a").Block("bob");

        Assert.Equal(204, Status(await Friends(db, "b").Block("alice"))); // bob can pre-empt alice lifting hers
        Assert.Equal(2, await db.UserBlocks.CountAsync());
        await Friends(db, "a").Unblock("bob");

        Assert.Equal(404, Status(await Send(db, "a", "bob"))); // bob's block still stands
        Assert.Equal(404, Status(await Profiles(db, "a").Get("bob")));
        Assert.Equal(1, await db.UserBlocks.CountAsync(b => b.BlockerId == "b"));
    }

    internal static string Reply(IActionResult r) => $"{Status(r)} {(r is ObjectResult o ? Json(r).ToString() : "")}";

    [Fact]
    public async Task Someone_who_blocked_you_answers_exactly_like_an_unknown_name_on_every_route()
    {
        using var db = await Db();
        await Friends(db, "c").Block("alice"); // carol blocked alice; alice must not be able to tell

        Assert.Equal(Reply(await Send(db, "a", "nobody")), Reply(await Send(db, "a", "carol")));
        // unfriend follows the visibility rule too: carol blocked alice, so she looks exactly like an unknown name; Block and Unblock
        // stay 204 for anyone who exists, so they reveal nothing
        Assert.Equal(Reply(await Friends(db, "a").Unfriend("nobody")), Reply(await Friends(db, "a").Unfriend("carol")));
        Assert.Equal(404, Status(await Friends(db, "a").Unfriend("carol")));
        Assert.Equal(204, Status(await Friends(db, "a").Unfriend("bob"))); // visible but not a friend: nothing to undo
        Assert.Equal(204, Status(await Friends(db, "a").Unblock("carol")));
        Assert.Equal(Reply(await Profiles(db, "a").Get("nobody")), Reply(await Profiles(db, "a").Get("carol")));
        Assert.Equal(404, Status(await Profiles(db, "a").Get("carol")));

        Assert.Equal(1, await db.UserBlocks.CountAsync());
        Assert.Equal(0, await db.Friendships.CountAsync());
        Assert.Equal(200, Status(await Profiles(db, null).Get("alice"))); // anonymous viewers have no identity to block
        Assert.Equal(200, Status(await Profiles(db, "c").Get("alice"))); // the blocker still sees the blocked
    }

    [Fact]
    public async Task Blocking_tells_the_blocked_user_nothing()
    {
        using var db = await Db();
        await Friends(db, "b").Block("alice");
        Assert.Empty(db.Notifications);
        Assert.Equal(0, Json(await Bell(db, "a").UnreadCount()).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Disabled_notification_types_create_nothing()
    {
        using var db = await Db();
        var prefs = await Bell(db, "b").Preferences();
        Assert.All(Json(prefs).EnumerateArray(), p => Assert.True(p.GetProperty("enabled").GetBoolean())); // default: all on
        Assert.Equal(Enum.GetValues<NotificationType>().Length, Json(prefs).GetArrayLength());

        Assert.Equal(200, Status(await Bell(db, "b").SetPreference("FriendRequest", new PreferenceRequest(false))));
        await Send(db, "a", "bob");
        Assert.Empty(db.Notifications); // the request exists, the bell stays quiet
        Assert.Single(db.Friendships);
        Assert.Contains(Json(await Bell(db, "b").Preferences()).EnumerateArray(), p => p.GetProperty("type").GetString() == "FriendRequest" && !p.GetProperty("enabled").GetBoolean());

        // the sender turns off acceptance notices
        await Bell(db, "a").SetPreference("FriendAccepted", new PreferenceRequest(false));
        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);
        Assert.Empty(db.Notifications);

        // turning it back on works, and it is per user
        await Bell(db, "b").SetPreference("friendrequest", new PreferenceRequest(true));
        await Send(db, "c", "bob");
        Assert.Single(db.Notifications);
        Assert.Equal(1, await db.NotificationPreferences.CountAsync(p => p.UserId == "b"));
    }

    [Theory]
    [InlineData("Nope")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Unknown_preference_types_are_404(string type)
    {
        using var db = await Db();
        Assert.Equal(404, Status(await Bell(db, "a").SetPreference(type, new PreferenceRequest(false))));
        Assert.Empty(db.NotificationPreferences);
    }

    [Fact]
    public async Task Notifications_page_newest_first_by_keyset_and_are_private()
    {
        using var db = await Db();
        for (var i = 0; i < 25; i++)
            db.Notifications.Add(new Notification { UserId = "a", Type = NotificationType.FriendAccepted, ActorId = "b", CreatedAt = DateTime.UtcNow.AddMinutes(i) });
        db.Notifications.Add(new Notification { UserId = "c", Type = NotificationType.FriendAccepted, ActorId = "b" });
        await db.SaveChangesAsync();

        var first = Json(await Bell(db, "a").List(null));
        var items = first.GetProperty("items");
        Assert.Equal(20, items.GetArrayLength());
        Assert.Equal("Bob", items[0].GetProperty("actor").GetString());
        var ids = items.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(ids.OrderByDescending(x => x), ids);

        var cursor = first.GetProperty("nextCursor").GetInt32();
        var second = Json(await Bell(db, "a").List(cursor));
        var rest = second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(5, rest.Count);
        Assert.True(rest.Max() < ids.Min()); // no overlap, nothing skipped
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        // exactly one page: no cursor
        Assert.Equal(JsonValueKind.Null, Json(await Bell(db, "c").List(null)).GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Unread_count_mark_one_and_mark_all()
    {
        using var db = await Db();
        db.Notifications.AddRange(
            new Notification { UserId = "a", Type = NotificationType.FriendAccepted, ActorId = "b" },
            new Notification { UserId = "a", Type = NotificationType.FriendAccepted, ActorId = "c" },
            new Notification { UserId = "b", Type = NotificationType.FriendAccepted, ActorId = "c" });
        await db.SaveChangesAsync();
        var mine = (await db.Notifications.Where(n => n.UserId == "a").ToListAsync()).Select(n => n.Id).ToList();
        var bobs = (await db.Notifications.SingleAsync(n => n.UserId == "b")).Id;
        async Task<int> Count(string u) => Json(await Bell(db, u).UnreadCount()).GetProperty("count").GetInt32();

        Assert.Equal(2, await Count("a"));
        Assert.Equal(404, Status(await Bell(db, "a").MarkRead(bobs))); // someone else's
        Assert.Equal(404, Status(await Bell(db, "a").MarkRead(9999)));
        Assert.Equal(204, Status(await Bell(db, "a").MarkRead(mine[0])));
        Assert.Equal(1, await Count("a"));
        Assert.Equal(204, Status(await Bell(db, "a").MarkRead(mine[0]))); // again is fine
        Assert.Equal(204, Status(await Bell(db, "a").MarkAllRead()));
        Assert.Equal(0, await Count("a"));
        Assert.Equal(1, await Count("b")); // mark-all only touches the caller
    }

    [Fact]
    public async Task A_request_notification_is_actionable_only_while_pending()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        var id = (await db.Friendships.SingleAsync()).Id;

        var item = Json(await Bell(db, "b").List(null)).GetProperty("items")[0];
        Assert.True(item.GetProperty("actionable").GetBoolean());
        Assert.Equal(id, item.GetProperty("requestId").GetInt32());

        await Friends(db, "b").Accept(id);
        item = Json(await Bell(db, "b").List(null)).GetProperty("items")[0];
        Assert.False(item.GetProperty("actionable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("requestId").ValueKind);
        Assert.True(item.GetProperty("read").GetBoolean());
    }

    [Fact]
    public async Task Friends_list_shows_friends_and_both_pending_directions()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);
        await Send(db, "c", "alice"); // incoming for alice
        db.Users.Add(User("d", "dave"));
        await db.SaveChangesAsync();
        await Send(db, "a", "dave"); // outgoing for alice

        var list = Json(await Friends(db, "a").List());

        Assert.Equal(new[] { "Bob" }, list.GetProperty("friends").EnumerateArray().Select(x => x.GetProperty("userName").GetString()));
        Assert.Equal(new[] { "carol" }, list.GetProperty("incoming").EnumerateArray().Select(x => x.GetProperty("userName").GetString()));
        Assert.Equal(new[] { "dave" }, list.GetProperty("outgoing").EnumerateArray().Select(x => x.GetProperty("userName").GetString()));
        Assert.Empty(Json(await Friends(db, "d").List()).GetProperty("friends").EnumerateArray());
    }

    [Fact]
    public async Task Profile_shows_the_public_part_to_anyone_and_the_relationship_to_the_signed_in()
    {
        using var db = await Db();
        await Send(db, "a", "bob");

        var anon = Json(await Profiles(db, null).Get("bob"));
        Assert.Equal("Bob", anon.GetProperty("userName").GetString());
        Assert.Equal(0, anon.GetProperty("friendCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, anon.GetProperty("viewerState").ValueKind);
        // nothing private is sent
        Assert.DoesNotContain("example.com", anon.ToString());
        Assert.False(anon.TryGetProperty("email", out _) || anon.TryGetProperty("id", out _));
        Assert.Equal(JsonValueKind.Null, anon.GetProperty("requestId").ValueKind);

        Assert.Equal("OutgoingPending", Json(await Profiles(db, "a").Get("bob")).GetProperty("viewerState").GetString());
        var incoming = Json(await Profiles(db, "b").Get("alice"));
        Assert.Equal("IncomingPending", incoming.GetProperty("viewerState").GetString());
        Assert.Equal(JsonValueKind.Number, incoming.GetProperty("requestId").ValueKind);
        Assert.Equal("None", Json(await Profiles(db, "c").Get("bob")).GetProperty("viewerState").GetString());
        Assert.Equal("Self", Json(await Profiles(db, "b").Get("bob")).GetProperty("viewerState").GetString());

        await Friends(db, "b").Accept((await db.Friendships.SingleAsync()).Id);
        var friends = Json(await Profiles(db, "a").Get("BOB"));
        Assert.Equal("Friends", friends.GetProperty("viewerState").GetString());
        Assert.Equal(1, friends.GetProperty("friendCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, friends.GetProperty("requestId").ValueKind);

        Assert.Equal(404, Status(await Profiles(db, null).Get("nobody")));
        Assert.Equal(404, Status(await Profiles(db, null).Get("bad name")));
    }

    [Fact]
    public async Task A_token_that_did_not_authenticate_gets_401_so_the_client_refreshes()
    {
        using var db = await Db();
        var c = Profiles(db, null);
        c.Request.Headers.Authorization = "Bearer expired";
        Assert.Equal(401, Status(await c.Get("bob")));
    }

    // pending requests to carol ("c" is always the low side against "x..."), sent `daysAgo` days ago, each with her notification
    static async Task Flood(AppDbContext db, int count, int daysAgo, string prefix = "x")
    {
        var rows = Enumerable.Range(0, count).Select(i => new Friendship
            { UserLowId = "c", UserHighId = prefix + i, RequesterId = prefix + i, Status = FriendStatus.Pending, CreatedAt = DateTime.UtcNow.AddDays(-daysAgo) }).ToList();
        db.Friendships.AddRange(rows);
        await db.SaveChangesAsync(); // ids first, for the notifications
        db.Notifications.AddRange(rows.Select(f => new Notification { UserId = "c", Type = NotificationType.FriendRequest, ActorId = "a", SubjectId = f.Id }));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_full_inbox_refuses_with_a_generic_answer_and_the_owner_can_still_ask_others()
    {
        using var db = await Db();
        await Flood(db, FriendsController.MaxPendingIncoming, daysAgo: 1);

        var r = await Send(db, "a", "carol");

        Assert.Equal(409, Status(r));
        Assert.DoesNotContain("pending", Json(r).GetProperty("message").GetString()!); // doesn't say why
        Assert.Equal(FriendsController.MaxPendingIncoming, await db.Friendships.CountAsync()); // nothing was added
        Assert.Equal(200, Status(await Send(db, "c", "alice")));
    }

    [Fact]
    public async Task Expired_requests_are_pruned_before_a_full_inbox_refuses_anyone()
    {
        using var db = await Db();
        await Flood(db, FriendsController.MaxPendingIncoming, daysAgo: SocialRules.RequestExpiryDays + 1);

        Assert.Equal(200, Status(await Send(db, "a", "carol")));

        Assert.Equal(1, await db.Friendships.CountAsync()); // the stale ones went, with their notifications
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task One_sender_can_only_have_so_many_requests_out_and_expired_ones_do_not_count()
    {
        using var db = await Db();
        for (var i = 0; i < FriendsController.MaxOutgoing; i++)
            db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "y" + i, RequesterId = "a", Status = FriendStatus.Pending });
        await db.SaveChangesAsync();

        var r = await Send(db, "a", "carol");
        Assert.Equal(409, Status(r));
        Assert.Contains("Cancel some", Json(r).GetProperty("message").GetString());
        Assert.Equal(200, Status(await Send(db, "b", "carol"))); // other senders are unaffected

        foreach (var f in db.Friendships.Where(f => f.RequesterId == "a").ToList()) f.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await db.SaveChangesAsync();
        Assert.Equal(200, Status(await Send(db, "a", "carol"))); // pruned first, then allowed
        Assert.Equal(2, await db.Friendships.CountAsync());
    }

    [Fact]
    public async Task Reading_the_lists_prunes_expired_pending_requests_but_not_friends()
    {
        using var db = await Db();
        await Flood(db, 3, daysAgo: 40);
        db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "c", RequesterId = "a", Status = FriendStatus.Accepted, CreatedAt = DateTime.UtcNow.AddDays(-400) });
        db.Users.Add(User("z0", "zed"));
        await Flood(db, 1, daysAgo: 2, prefix: "z");
        await db.SaveChangesAsync();

        var list = Json(await Friends(db, "c").List());

        Assert.Equal(1, list.GetProperty("incoming").GetArrayLength());
        Assert.Equal(1, list.GetProperty("friends").GetArrayLength()); // old, but accepted: kept
        Assert.Equal(1 + 1, await db.Friendships.CountAsync());
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Decline_all_clears_every_pending_request_to_me_and_nothing_else()
    {
        using var db = await Db();
        await Flood(db, 5, daysAgo: 1);
        db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "c", RequesterId = "a", Status = FriendStatus.Accepted }); // a friend: stays
        db.Friendships.Add(new Friendship { UserLowId = "c", UserHighId = "d", RequesterId = "c", Status = FriendStatus.Pending }); // sent by carol: stays
        db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "b", RequesterId = "a", Status = FriendStatus.Pending }); // someone else's inbox: stays
        await db.SaveChangesAsync();

        Assert.Equal(204, Status(await Friends(db, "c").DeclineAll()));

        var left = await db.Friendships.ToListAsync();
        Assert.Equal(3, left.Count);
        Assert.DoesNotContain(left, f => f.Status == FriendStatus.Pending && f.RequesterId != "c" && f.RequesterId != "a");
        Assert.Empty(db.Notifications); // their notifications went with them
        Assert.Equal(204, Status(await Friends(db, "c").DeclineAll())); // nothing left: still fine
        // and nobody was told: no notification exists for any requester
        Assert.DoesNotContain(await db.Notifications.ToListAsync(), n => n.UserId != "c");
    }

    [Fact]
    public async Task A_request_cannot_be_accepted_while_a_block_exists_either_way()
    {
        using var db = await Db();
        await Send(db, "a", "bob");
        db.UserBlocks.Add(new UserBlock { BlockerId = "b", BlockedId = "a" }); // as if it landed after the request was read
        await db.SaveChangesAsync();
        var id = (await db.Friendships.SingleAsync()).Id;

        Assert.Equal(404, Status(await Friends(db, "b").Accept(id)));
        db.UserBlocks.RemoveRange(db.UserBlocks);
        db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "b" });
        await db.SaveChangesAsync();
        Assert.Equal(404, Status(await Friends(db, "b").Accept(id)));
        Assert.Equal(FriendStatus.Pending, (await db.Friendships.SingleAsync()).Status);
    }

    [Fact]
    public void A_trailing_newline_is_not_a_valid_username()
    {
        Assert.False(SocialRules.ValidUserName("bob\n"));
        Assert.True(SocialRules.ValidUserName("bob"));
    }

    [Fact]
    public async Task Usernames_are_found_exactly_and_case_insensitively()
    {
        using var db = await Db();
        Assert.Equal("b", (await SocialRules.FindByName(db, " bOb "))!.Id);
        Assert.Null(await SocialRules.FindByName(db, "bo"));
        Assert.Null(await SocialRules.FindByName(db, "%"));
        Assert.Null(await SocialRules.FindByName(db, new string('a', 300)));
    }

    [Fact]
    public void Rate_limit_is_per_user_and_says_when_to_retry()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => SocialRules.PerUser(ctx, 2, TimeSpan.FromHours(1)));
        HttpContext Ctx(string id) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", id) }, "test")) };

        using var one = limiter.AttemptAcquire(Ctx("a"));
        using var two = limiter.AttemptAcquire(Ctx("a"));
        using var three = limiter.AttemptAcquire(Ctx("a"));
        using var other = limiter.AttemptAcquire(Ctx("b"));

        Assert.True(one.IsAcquired && two.IsAcquired);
        Assert.False(three.IsAcquired);
        Assert.True(three.TryGetMetadata(MetadataName.RetryAfter, out var after) && after > TimeSpan.Zero); // becomes the Retry-After header
        Assert.True(other.IsAcquired); // another user is unaffected
    }
}
