using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// bio and profile picture: censor and length, upload checks, the public picture route, deletion, and the admin clear action.
// Users: a alice, b Bob, c carol, d dave (admin).
public class ProfileTests
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    static readonly byte[] Webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 1];

    static async Task<IActionResult> Mine(Func<AppDbContext> open, string me, Func<MyProfileController, Task<IActionResult>> f, byte[]? body = null)
    {
        using var db = open();
        var c = As(new MyProfileController(db), me);
        c.Request.Body = new MemoryStream(body ?? []);
        return await f(c);
    }

    static Task<IActionResult> Bio(Func<AppDbContext> open, string me, string? bio) => Mine(open, me, c => c.SetBio(new BioRequest(bio)));
    static Task<IActionResult> Upload(Func<AppDbContext> open, string me, byte[] bytes) => Mine(open, me, c => c.SetAvatar(), bytes);

    static async Task<IActionResult> Pic(Func<AppDbContext> open, string name)
    {
        using var db = open();
        return await As(new ProfilesController(db), null).Avatar(name);
    }

    static async Task<AppUser> Load(Func<AppDbContext> open, string id)
    {
        using var db = open();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    static async Task<int> AvatarCount(Func<AppDbContext> open)
    {
        using var db = open();
        return await db.Avatars.CountAsync();
    }

    [Fact]
    public async Task Bio_is_trimmed_censored_limited_and_clearable()
    {
        var open = Mem();
        await Seed(open);

        Assert.Equal(200, Status(await Bio(open, "a", "  I love anime, you faggots  ")));
        Assert.Equal("I love anime, you *******", (await Load(open, "a")).Bio);

        Assert.Equal(200, Status(await Bio(open, "a", new string('x', Avatars.MaxBio))));
        Assert.Equal(400, Status(await Bio(open, "a", new string('x', Avatars.MaxBio + 1))));
        Assert.Equal(Avatars.MaxBio, (await Load(open, "a")).Bio!.Length); // the rejected one changed nothing

        Assert.Equal(200, Status(await Bio(open, "a", "   ")));
        Assert.Null((await Load(open, "a")).Bio);
    }

    [Fact]
    public async Task Profile_shows_the_bio_but_not_to_someone_who_blocked_the_viewer()
    {
        var open = Mem();
        await Seed(open);
        await Bio(open, "a", "hello there");
        await Bio(open, "b", "bob's bio");
        using (var db = open()) { db.UserBlocks.Add(new UserBlock { BlockerId = "b", BlockedId = "c" }); await db.SaveChangesAsync(); }

        Assert.Equal("hello there", Json(await Profile(open, "c", "alice")).GetProperty("bio").GetString());
        Assert.Equal(404, Status(await Profile(open, "c", "Bob"))); // Bob blocked carol: no profile, so no bio
    }

    static async Task<IActionResult> Profile(Func<AppDbContext> open, string? me, string name)
    {
        using var db = open();
        return await Profiles(db, me).Get(name);
    }

    [Fact]
    public Task Upload_accepts_png_jpeg_webp_by_their_bytes_and_rejects_the_rest() => Uploads(Mem());

    internal static async Task Uploads(Func<AppDbContext> open)
    {
        await Seed(open);

        foreach (var (bytes, type) in new[] { (Png, "image/png"), (Jpeg, "image/jpeg"), (Webp, "image/webp") })
        {
            Assert.Equal(204, Status(await Upload(open, "a", bytes)));
            Assert.Equal(type, ((FileContentResult)await Pic(open, "alice")).ContentType);
        }
        Assert.Equal(1, await AvatarCount(open)); // replaced, not added

        Assert.Equal(400, Status(await Upload(open, "b", "<svg onload=alert(1)>"u8.ToArray())));
        Assert.Equal(400, Status(await Upload(open, "b", "GIF89a......"u8.ToArray())));
        Assert.Equal(400, Status(await Upload(open, "b", []))); // empty
        Assert.Equal(400, Status(await Upload(open, "b", Png.Take(4).ToArray()))); // cut off in the signature
        var big = new byte[Avatars.MaxBytes + 1];
        Png.CopyTo(big, 0);
        Assert.Equal(400, Status(await Upload(open, "b", big))); // right type, too big
        Assert.Equal(1, await AvatarCount(open));
    }

    [Fact]
    public async Task Picture_route_is_public_cached_by_etag_and_gone_with_no_picture_or_account()
    {
        var open = Mem();
        await Seed(open);
        Assert.Equal(404, Status(await Pic(open, "alice"))); // none yet: the client draws the initial letter
        await Upload(open, "a", Png);

        var file = (FileContentResult)await Pic(open, "ALICE"); // the name lookup ignores case
        Assert.Equal(Png, file.FileContents);
        Assert.Equal(404, Status(await Pic(open, "carol"))); // no picture
        // a matching If-None-Match gets a 304 with no body; a stale one gets the picture
        string Etag(AppDbContext db) => $"\"{db.Avatars.AsNoTracking().Single().UpdatedAt.Ticks}\"";
        using (var db = open())
        {
            var c = As(new ProfilesController(db), null);
            c.Request.Headers.IfNoneMatch = Etag(db);
            Assert.Equal(304, Status(await c.Avatar("alice")));
            Assert.Equal(Etag(db), c.Response.Headers.ETag.ToString());
            c.Request.Headers.IfNoneMatch = "\"1\"";
            Assert.IsType<FileContentResult>(await c.Avatar("alice"));
        }
        Assert.True(Json(await Profile(open, "c", "alice")).GetProperty("hasAvatar").GetBoolean());

        await Mine(open, "a", c => c.RemoveAvatar());
        Assert.Equal(404, Status(await Pic(open, "alice")));
        Assert.Equal(404, Status(await Pic(open, "bad name!")));
    }

    [Fact]
    public Task Deleting_the_account_clears_bio_and_picture() => DeletionClears(Mem());

    internal static async Task DeletionClears(Func<AppDbContext> open)
    {
        await Seed(open);
        await Bio(open, "a", "about me");
        await Upload(open, "a", Png);
        await Upload(open, "b", Png);

        using (var db = open()) Assert.True(await AccountDeletion.Delete(db, "a"));

        Assert.Null((await Load(open, "a")).Bio);
        Assert.Equal(1, await AvatarCount(open)); // only Bob's is left
        Assert.Equal(404, Status(await Pic(open, "deleted-a")));
    }

    static async Task<int> Report(Func<AppDbContext> open, string by, string user)
    {
        using var db = open();
        Assert.Equal(201, Status(await As(new ReportsController(db), by).ReportProfile(user, new ReportRequest("Hate", null))));
        return await db.Reports.MaxAsync(r => r.Id);
    }

    static async Task<IActionResult> Queue(Func<AppDbContext> open, Func<AdminReportsController, Task<IActionResult>> f)
    {
        using var db = open();
        return await f(As(new AdminReportsController(db), "d"));
    }

    [Fact]
    public Task Admin_clears_the_reported_bio_and_or_picture_and_the_reporters_are_told() => AdminClears(Mem());

    internal static async Task AdminClears(Func<AppDbContext> open)
    {
        await Seed(open);
        await Bio(open, "a", "bad bio");
        await Upload(open, "a", Png);
        var first = await Report(open, "b", "alice");
        var second = await Report(open, "c", "alice");

        // the queue shows what is on the profile now
        var item = Json(await Queue(open, q => q.List(null))).GetProperty("items")[0];
        Assert.Equal("bad bio", item.GetProperty("bio").GetString());
        Assert.True(item.GetProperty("hasAvatar").GetBoolean());

        Assert.Equal(400, Status(await Queue(open, q => q.ClearProfile(first, new ClearProfileRequest(false, false)))));
        Assert.Equal(204, Status(await Queue(open, q => q.ClearProfile(first, new ClearProfileRequest(true, false))))); // bio only
        Assert.Null((await Load(open, "a")).Bio);
        Assert.Equal(1, await AvatarCount(open));

        // both of the open reports on alice were settled and both reporters got the outcome
        using (var db = open())
        {
            Assert.All(await db.Reports.ToListAsync(), r => Assert.Equal(ReportStatus.Removed, r.Status));
            Assert.Equal(new[] { first, second }, await db.Notifications.Where(n => n.Type == NotificationType.ReportOutcome).Select(n => n.SubjectId!.Value).OrderBy(x => x).ToArrayAsync());
        }
        Assert.Equal(404, Status(await Queue(open, q => q.ClearProfile(first, new ClearProfileRequest(true, true))))); // already settled

        var third = await Report(open, "b", "alice");
        Assert.Equal(204, Status(await Queue(open, q => q.ClearProfile(third, new ClearProfileRequest(false, true))))); // picture only
        Assert.Equal(0, await AvatarCount(open));
    }

    [Fact]
    public async Task Clear_profile_refuses_reports_about_reviews()
    {
        var open = Mem();
        await Seed(open);
        using (var db = open())
        {
            db.Reports.Add(new Report { ReporterId = "b", Target = ReportTarget.Review, TargetId = 1, TargetUserId = "a", Reason = ReportReason.Spam, Snapshot = "x" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(400, Status(await Queue(open, q => q.ClearProfile(1, new ClearProfileRequest(true, true)))));
    }
}
