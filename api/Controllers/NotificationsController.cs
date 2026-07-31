using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record PreferenceRequest(bool Enabled);

[ApiController]
[Authorize]
[Route("me")]
public class NotificationsController : ControllerBase
{
    const int PageSize = 20;
    readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    string UserId => User.FindFirst("sub")!.Value;

    // keyset paging, newest first: pass nextCursor back as ?cursor= until it is null.
    // Ids grow with time, so ordering by id is ordering by creation without timestamp precision traps.
    [HttpGet("notifications")]
    public async Task<IActionResult> List(int? cursor)
    {
        var me = UserId;
        var q = _db.Notifications.Where(n => n.UserId == me);
        if (cursor != null) q = q.Where(n => n.Id < cursor);

        var rows = await q.OrderByDescending(n => n.Id).Take(PageSize + 1)
            .Select(n => new
            {
                n.Id, n.Type, n.SubjectId, n.CreatedAt,
                actor = n.Actor.DeletedAt != null ? AppUser.DeletedName : n.Actor.UserName,
                read = n.ReadAt != null,
                // a request can be answered only while it is still pending
                pending = n.Type == NotificationType.FriendRequest
                    && _db.Friendships.Any(f => f.Id == n.SubjectId && f.Status == FriendStatus.Pending)
            }).ToListAsync();

        var more = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();
        // Reply and Reaction notifications link to the review; resolved for this page only
        var replyIds = rows.Where(n => n.Type == NotificationType.Reply).Select(n => n.SubjectId).ToList();
        var replyReview = await _db.Replies.Where(x => replyIds.Contains(x.Id)).Select(x => new { x.Id, x.RatingId }).ToDictionaryAsync(x => x.Id, x => x.RatingId);
        var reportIds = rows.Where(n => n.Type == NotificationType.ReportOutcome).Select(n => n.SubjectId).ToList();
        var outcomes = await _db.Reports.Where(x => reportIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Status.ToString());
        var items = rows.Select(n => new
        {
            n.Id, type = n.Type.ToString(), n.actor, n.CreatedAt, n.read,
            actionable = n.pending,
            outcome = n.Type == NotificationType.ReportOutcome && n.SubjectId != null ? outcomes.GetValueOrDefault(n.SubjectId.Value) : null,
            requestId = n.pending ? n.SubjectId : null,
            reviewId = n.Type == NotificationType.Reaction ? n.SubjectId
                : n.Type == NotificationType.Reply && n.SubjectId != null && replyReview.TryGetValue(n.SubjectId.Value, out var rid) ? rid : (int?)null
        });
        return Ok(new { items, nextCursor = more ? rows[^1].Id : (int?)null });
    }

    [HttpGet("notifications/unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var me = UserId;
        return Ok(new { count = await _db.Notifications.CountAsync(n => n.UserId == me && n.ReadAt == null) });
    }

    [HttpPost("notifications/{id:int}/read")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var me = UserId;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me);
        if (n == null) return NotFound();
        n.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> MarkAllRead()
    {
        var me = UserId;
        // one UPDATE on Postgres; InMemory (the tests) can't run ExecuteUpdate, so it loads the rows there
        var unread = _db.Notifications.Where(n => n.UserId == me && n.ReadAt == null);
        var now = DateTime.UtcNow;
        if (_db.Database.IsNpgsql()) await unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
        else
        {
            foreach (var n in await unread.ToListAsync()) n.ReadAt = now;
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    // every type with its flag; no row means enabled
    [HttpGet("notification-preferences")]
    public async Task<IActionResult> Preferences()
    {
        var me = UserId;
        var off = await _db.NotificationPreferences.Where(p => p.UserId == me && !p.Enabled).Select(p => p.Type).ToListAsync();
        return Ok(Enum.GetValues<NotificationType>().Select(t => new { type = t.ToString(), enabled = !off.Contains(t) }));
    }

    [HttpPut("notification-preferences/{type}")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> SetPreference(string type, PreferenceRequest req)
    {
        // TryParse also accepts "1", so require a real name
        if (!Enum.TryParse<NotificationType>(type, true, out var t) || !Enum.IsDefined(t) || char.IsDigit(type[0]))
            return NotFound();

        var me = UserId;
        var pref = await _db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == me && p.Type == t);
        if (pref == null) _db.NotificationPreferences.Add(pref = new NotificationPreference { UserId = me, Type = t });
        pref.Enabled = req.Enabled;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) // two tabs inserted the same row at once
        {
            return Conflict(new { message = "Try again." });
        }
        return Ok(new { type = t.ToString(), enabled = req.Enabled });
    }
}
