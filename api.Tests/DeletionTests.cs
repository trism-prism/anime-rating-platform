using System.Text.RegularExpressions;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// Account deletion: the scenario runs on InMemory here and on real Postgres in DeletionPostgresTests; the controller rules
// (password, emailed code, last admin) run on InMemory with a recording mail sender.
public class DeletionTests
{
    static async Task<int> Rate(Func<AppDbContext> open, string who, int title, int score, string? text = null)
    {
        using var db = open();
        var r = Json(await As(new RatingsController(db), who).Put(title, new RatingRequest(score, score, score, score, text)));
        return r.GetProperty("id").GetInt32();
    }

    static async Task Ok(Task<IActionResult> call) => Assert.InRange(Status(await call), 200, 299);

    // rows of the personal kind that must be gone for the user
    static async Task<int> PersonalLeftovers(AppDbContext db, string u) =>
        await db.WatchList.CountAsync(x => x.UserId == u) + await db.CustomLists.CountAsync(x => x.UserId == u)
        + await db.Friendships.CountAsync(x => x.UserLowId == u || x.UserHighId == u) + await db.UserBlocks.CountAsync(x => x.BlockerId == u || x.BlockedId == u)
        + await db.Notifications.CountAsync(x => x.UserId == u || x.ActorId == u) + await db.NotificationPreferences.CountAsync(x => x.UserId == u)
        + await db.Sessions.CountAsync(x => x.UserId == u) + await db.UserRoles.CountAsync(x => x.UserId == u) + await db.UserLogins.CountAsync(x => x.UserId == u)
        + await db.UserClaims.CountAsync(x => x.UserId == u) + await db.UserTokens.CountAsync(x => x.UserId == u);

    // users a alice (deleted), b Bob, c carol, d dave (admin); titles 1..3 in series 1
    internal static async Task AnonymizesAndKeepsTheContent(Func<AppDbContext> open)
    {
        await Seed(open);
        // ratings through the real endpoint, so the title totals are real: T1 8+6+4, T2 10+2, T3 5 (alice only)
        var a1 = await Rate(open, "a", 1, 8, "alice on one");
        var b1 = await Rate(open, "b", 1, 6, "bob on one");
        var c1 = await Rate(open, "c", 1, 4, "carol on one");
        await Rate(open, "a", 2, 10);
        await Rate(open, "b", 2, 2);
        await Rate(open, "a", 3, 5);

        // replies, reactions and their notifications, in both directions
        await Ok(Call(open, "b", c => c.PostReply(a1, new ReplyRequest("bob replies to alice", false))));
        await Ok(Call(open, "b", c => c.React(a1, new ReactionRequest("Helpful"))));
        await Ok(Call(open, "a", c => c.PostReply(b1, new ReplyRequest("alice replies to bob", false))));
        await Ok(Call(open, "a", c => c.React(b1, new ReactionRequest("Helpful"))));
        await Ok(Call(open, "a", c => c.React(c1, new ReactionRequest("Unhelpful"))));
        await Ok(Call(open, "c", c => c.React(b1, new ReactionRequest("Funny")))); // survives

        using (var db = open())
        {
            (await db.Users.SingleAsync(x => x.Id == "a")).CreatedAt = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Utc);
            // friendship a-b came from Seed; carol asked alice; alice blocked dave
            db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "c", RequesterId = "c", Status = FriendStatus.Pending });
            await db.SaveChangesAsync();
            var f = await db.Friendships.SingleAsync(x => x.UserHighId == "c");
            db.Notifications.Add(new Notification { UserId = "a", Type = NotificationType.FriendRequest, ActorId = "c", SubjectId = f.Id });
            db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "d" });
            // lists, watch list, preferences and a session for alice and for bob
            foreach (var who in new[] { "a", "b" })
            {
                var list = new CustomList { UserId = who, Name = who + " list" };
                list.Items.Add(new CustomListItem { TitleId = 1 });
                db.CustomLists.Add(list);
                db.WatchList.Add(new WatchListEntry { UserId = who, TitleId = 2, Status = WatchStatus.Watching });
                db.NotificationPreferences.Add(new NotificationPreference { UserId = who, Type = NotificationType.Reaction, Enabled = false });
                db.Sessions.Add(new Session { Id = Guid.NewGuid(), UserId = who, ExpiresAt = DateTime.UtcNow.AddDays(1), CurrentTokenHash = who });
            }
            // reports: carol about alice's review, alice about bob's review, carol about bob's review
            db.Reports.Add(new Report { ReporterId = "c", Target = ReportTarget.Review, TargetId = a1, TargetUserId = "a", Reason = ReportReason.Spam, Snapshot = "alice on one" });
            db.Reports.Add(new Report { ReporterId = "a", Target = ReportTarget.Review, TargetId = b1, TargetUserId = "b", Reason = ReportReason.Spam, Snapshot = "bob on one" });
            db.Reports.Add(new Report { ReporterId = "c", Target = ReportTarget.Review, TargetId = b1, TargetUserId = "b", Reason = ReportReason.Hate, Snapshot = "bob on one" });
            db.Reports.Add(new Report { ReporterId = "c", Target = ReportTarget.Profile, TargetId = 0, TargetUserId = "a", Reason = ReportReason.Spam, Snapshot = "Profile of alice" });
            db.UserClaims.Add(new Microsoft.AspNetCore.Identity.IdentityUserClaim<string> { UserId = "a", ClaimType = "x", ClaimValue = "y" });
            db.UserTokens.Add(new Microsoft.AspNetCore.Identity.IdentityUserToken<string> { UserId = "a", LoginProvider = "p", Name = "n", Value = "v" });
            db.UserLogins.Add(new Microsoft.AspNetCore.Identity.IdentityUserLogin<string> { UserId = "a", LoginProvider = "Google", ProviderKey = "g-a" });
            await db.SaveChangesAsync();
        }
        using (var db = open())
        {
            Assert.Equal(5.67, RatingStats.SeriesAverage((await db.Titles.ToListAsync()).Select(t => (t.RatingCount, t.RatingSum))));
            Assert.True(await PersonalLeftovers(db, "a") > 0);
        }

        using (var db = open()) Assert.True(await AccountDeletion.Delete(db, "a"));
        using (var db = open()) Assert.False(await AccountDeletion.Delete(db, "a")); // already deleted

        using (var db = open())
        {
            Assert.Equal(0, await PersonalLeftovers(db, "a"));
            // the tombstone: no personal data, cannot sign in, locked
            var gone = await db.Users.SingleAsync(x => x.Id == "a");
            Assert.NotNull(gone.DeletedAt);
            Assert.Null(gone.Email); Assert.Null(gone.NormalizedEmail); Assert.Null(gone.PasswordHash); Assert.False(gone.EmailConfirmed);
            Assert.DoesNotContain("alice", gone.UserName!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), gone.CreatedAt); // only the month joined is kept
            Assert.True(gone.LockoutEnd > DateTimeOffset.UtcNow.AddYears(100));
            // everything alice wrote is still there and the totals did not move
            var titles = await db.Titles.OrderBy(t => t.Id).ToListAsync();
            Assert.Equal(new[] { (3, 18.0), (2, 12.0), (1, 5.0) }, titles.Select(t => (t.RatingCount, t.RatingSum)));
            Assert.Equal(5.67, RatingStats.SeriesAverage(titles.Select(t => (t.RatingCount, t.RatingSum))));
            Assert.Equal(3, await db.Ratings.CountAsync(r => r.UserId == "a"));
            Assert.Equal(2, await db.Replies.CountAsync());
            Assert.Equal(4, await db.Reactions.CountAsync());
            var bobs = await db.Ratings.SingleAsync(r => r.Id == b1);
            Assert.Equal((1, 0, 1, 1), (bobs.HelpfulCount, bobs.UnhelpfulCount, bobs.FunnyCount, bobs.Score));
            // reports stay (filed by alice, or about her content); only the one about her profile goes
            Assert.Equal(3, await db.Reports.CountAsync());
            Assert.Equal(1, await db.Reports.CountAsync(r => r.ReporterId == "a")); // the report she filed stays
            Assert.Equal(0, await db.Reports.CountAsync(r => r.Target == ReportTarget.Profile));
            // bob keeps the bell entry for carol's reaction, none from alice
            var one = Assert.Single(await db.Notifications.Where(n => n.UserId == "b").ToListAsync());
            Assert.Equal((NotificationType.Reaction, "c", b1), (one.Type, one.ActorId, one.SubjectId));
            // other people's data is untouched
            Assert.Equal(1, await db.CustomLists.CountAsync(l => l.UserId == "b"));
            Assert.Equal(1, await db.Sessions.CountAsync(x => x.UserId == "b"));
            Assert.True(await db.Users.AnyAsync(u => u.Id == "d"));
        }

        // every place a name shows reads "Deleted user", and her profile is gone
        using (var db = open())
        {
            var reviews = Json(await Call(open, "b", c => c.ForTitle(1, null, null))).GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(reviews, r => r.GetProperty("id").GetInt32() == a1 && r.GetProperty("userName").GetString() == AppUser.DeletedName);
            var replies = Json(await Call(open, "b", c => c.Replies(b1, null))).GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(AppUser.DeletedName, Assert.Single(replies).GetProperty("userName").GetString());
            Assert.Equal(404, Status(await Profiles(db, "b").Get("alice")));
            Assert.Equal(404, Status(await Profiles(db, "b").Get("deleted-a")));
        }
    }

    [Fact]
    public async Task Deletion_anonymizes_the_account_and_keeps_its_content() => await AnonymizesAndKeepsTheContent(Mem());

    [Fact]
    public async Task Deleting_an_unknown_account_does_nothing()
    {
        var open = Mem();
        await Seed(open);
        using var db = open();
        Assert.False(await AccountDeletion.Delete(db, "nobody"));
        Assert.Equal(4, await db.Users.CountAsync(u => u.DeletedAt == null));
    }

    // ---- the controller ----

    static DeleteAccountController Controller(TestHost h, AppUser me) =>
        h.As(new DeleteAccountController(h.Db, h.Users, h.Email, h.Sessions, new CredentialCheck(h.Users), h.Cache, NullLogger<DeleteAccountController>.Instance), me);

    [Fact]
    public async Task A_password_account_needs_the_right_password_and_is_told_by_email()
    {
        using var h = new TestHost();
        var kai = await h.AddUser("kai");
        var other = await h.AddUser("other");

        Assert.Equal(403, Status(await Controller(h, kai).Delete(new DeleteAccountRequest("wrong-Passw0rd!", null))));
        Assert.Equal(403, Status(await Controller(h, kai).Delete(new DeleteAccountRequest(null, null))));
        Assert.NotNull(await h.Users.FindByIdAsync(kai.Id));
        Assert.Empty(h.Mails);

        Assert.Equal(204, Status(await Controller(h, kai).Delete(new DeleteAccountRequest("Passw0rd1!", null))));
        Assert.NotNull((await h.Users.FindByIdAsync(kai.Id))!.DeletedAt);
        Assert.Null(await h.Users.FindByEmailAsync("kai@example.com")); // cannot sign in with it any more
        Assert.NotNull(await h.Users.FindByIdAsync(other.Id));
        var mail = Assert.Single(h.Mails);
        Assert.Equal("kai@example.com", mail.To);
        Assert.Contains("deleted", mail.Subject);
    }

    [Fact]
    public async Task A_google_only_account_needs_a_fresh_emailed_code()
    {
        using var h = new TestHost();
        var sam = await h.AddUser("sam", password: null);

        Assert.Equal(400, Status(await Controller(h, sam).Delete(new DeleteAccountRequest(null, null)))); // no code
        Assert.Equal(400, Status(await Controller(h, sam).Delete(new DeleteAccountRequest(null, "000000"))));
        Assert.NotNull(await h.Users.FindByIdAsync(sam.Id));

        Assert.Equal(200, Status(await Controller(h, sam).SendCode()));
        Assert.Equal(429, Status(await Controller(h, sam).SendCode())); // one a minute
        var code = Regex.Match(Assert.Single(h.Mails).Body, @"is (\d+)\.").Groups[1].Value;
        Assert.Equal(204, Status(await Controller(h, sam).Delete(new DeleteAccountRequest(null, code))));
        Assert.NotNull((await h.Users.FindByIdAsync(sam.Id))!.DeletedAt);
    }

    [Fact]
    public async Task A_code_for_another_purpose_does_not_delete_and_password_accounts_do_not_get_codes()
    {
        using var h = new TestHost();
        var sam = await h.AddUser("sam", password: null);
        await h.Email.SendVerificationCode(h.Users, sam);
        var verifyCode = Regex.Match(h.Mails.Single().Body, @"is (\d+)\.").Groups[1].Value;
        Assert.Equal(400, Status(await Controller(h, sam).Delete(new DeleteAccountRequest(null, verifyCode))));
        Assert.NotNull(await h.Users.FindByIdAsync(sam.Id));

        var kai = await h.AddUser("kai");
        Assert.Equal(400, Status(await Controller(h, kai).SendCode()));
    }

    [Fact]
    public async Task The_only_admin_cannot_delete_but_one_of_two_can()
    {
        using var h = new TestHost();
        var boss = await h.AddUser("boss", admin: true);
        Assert.Equal(409, Status(await Controller(h, boss).Delete(new DeleteAccountRequest("Passw0rd1!", null))));
        Assert.NotNull(await h.Users.FindByIdAsync(boss.Id));

        await h.AddUser("boss2", admin: true);
        Assert.Equal(204, Status(await Controller(h, boss).Delete(new DeleteAccountRequest("Passw0rd1!", null))));
    }
}
