using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record ReportRequest(string? Reason, string? Note);

// Reporting a review, a reply or a profile. The reporter must be able to see the target (audience and blocks apply, so a hidden
// review is the same 404 as a missing one), can't report their own content, and has one open report per target.
[ApiController]
[Authorize]
[EnableRateLimiting("report")]
public class ReportsController : ControllerBase
{
    public const int MaxNote = 500;
    readonly AppDbContext _db;

    public ReportsController(AppDbContext db) => _db = db;

    string Me => User.FindFirst("sub")!.Value;

    [HttpPost("reviews/{id:int}/report")]
    public Task<IActionResult> ReportReview(int id, ReportRequest body) => Submit(body, async me =>
    {
        var r = await ReviewRules.FindReview(_db, id, me, false);
        return r == null ? null : (ReportTarget.Review, id, r.UserId, r.ReviewText!);
    });

    [HttpPost("replies/{id:int}/report")]
    public Task<IActionResult> ReportReply(int id, ReportRequest body) => Submit(body, async me =>
    {
        var x = await _db.Replies.Where(r => r.Id == id).VisibleTo(_db, me, false).FirstOrDefaultAsync();
        // the reply follows its review's audience
        if (x == null || await ReviewRules.FindReview(_db, x.RatingId, me, false) == null) return null;
        return (ReportTarget.Reply, id, x.UserId, x.Body);
    });

    [HttpPost("users/{username}/report")]
    public Task<IActionResult> ReportProfile(string username, ReportRequest body) => Submit(body, async me =>
    {
        var u = await SocialRules.FindVisible(_db, me, username);
        return u == null ? null : (ReportTarget.Profile, 0, u.Id, "Profile of " + u.UserName);
    });

    // find returns the target (null = not visible or missing)
    async Task<IActionResult> Submit(ReportRequest body, Func<string, Task<(ReportTarget, int, string, string)?>> find)
    {
        if (!ReviewRules.TryName<ReportReason>(body.Reason, out var reason))
            return BadRequest(new { message = "Reason must be Spam, Harassment, Hate, Spoiler or Other." });
        var note = body.Note?.Trim();
        if (note?.Length > MaxNote) return BadRequest(new { message = $"The note must be {MaxNote} characters or fewer." });
        var me = Me;
        if (!await _db.Users.AnyAsync(u => u.Id == me && u.EmailConfirmed))
            return StatusCode(403, new { message = "Verify your email to report." });

        var found = await find(me);
        if (found == null) return NotFound();
        var (target, id, authorId, snapshot) = found.Value;
        if (authorId == me) return BadRequest(new { message = "You can't report your own content." });
        if (await _db.Reports.AnyAsync(r => r.ReporterId == me && r.Target == target && r.TargetId == id && r.TargetUserId == authorId && r.Status == ReportStatus.Open))
            return Conflict(new { message = "You already reported this." });

        var report = new Report
        {
            ReporterId = me, Target = target, TargetId = id, TargetUserId = authorId, Reason = reason,
            Note = string.IsNullOrEmpty(note) ? null : note, Snapshot = snapshot
        };
        _db.Reports.Add(report);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) // a double click filed it first (unique index), or the target's author is gone
        {
            _db.ChangeTracker.Clear();
            return Conflict(new { message = "You already reported this." });
        }
        return StatusCode(201, new { report.Id });
    }
}
