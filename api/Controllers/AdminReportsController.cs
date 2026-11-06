using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

// The queue of open reports, oldest first. Resolving one tells its reporter (if their preferences allow).
[ApiController]
[Authorize]
[AdminOnly]
[Route("admin/reports")]
public class AdminReportsController : ControllerBase
{
    const int PageSize = 20;
    readonly AppDbContext _db;

    public AdminReportsController(AppDbContext db) => _db = db;

    // keyset by id: pass nextCursor back as ?cursor= until it is null
    [HttpGet]
    public async Task<IActionResult> List(int? cursor)
    {
        var q = _db.Reports.Where(r => r.Status == ReportStatus.Open);
        if (cursor != null) q = q.Where(r => r.Id > cursor);
        var rows = await q.OrderBy(r => r.Id).Take(PageSize + 1).ToListAsync();
        var more = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();

        var ids = rows.SelectMany(r => new[] { r.ReporterId, r.TargetUserId }).Distinct().ToList();
        var names = await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName!);
        return Ok(new
        {
            items = rows.Select(r => new
            {
                r.Id, target = r.Target.ToString(), r.TargetId, reason = r.Reason.ToString(), r.Note, r.Snapshot, r.CreatedAt,
                reporter = names.GetValueOrDefault(r.ReporterId), targetUser = names.GetValueOrDefault(r.TargetUserId)
            }),
            nextCursor = more ? rows[^1].Id : (int?)null
        });
    }

    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(x => x.Id == id && x.Status == ReportStatus.Open);
        if (r == null) return NotFound();
        await Resolve(r, ReportStatus.Dismissed);
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound(); // false: someone resolved it first
    }

    // Removes the content (same code as the direct admin routes) and settles every open report on it, so none is left pointing at nothing.
    [HttpPost("{id:int}/remove")]
    public async Task<IActionResult> Remove(int id)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(x => x.Id == id && x.Status == ReportStatus.Open);
        if (r == null) return NotFound();
        if (r.Target == ReportTarget.Profile) return BadRequest(new { message = "A profile has no content to remove yet. Dismiss the report." });

        // false means it was already gone (deleted by its author): the report still counts as handled
        if (r.Target == ReportTarget.Review) await SocialRules.RemoveReviewText(_db, r.TargetId);
        else await SocialRules.RemoveReply(_db, r.TargetId);
        var same = await _db.Reports.Where(x => x.Status == ReportStatus.Open && x.Target == r.Target && x.TargetId == r.TargetId && x.TargetUserId == r.TargetUserId).ToListAsync();
        foreach (var x in same) await Resolve(x, ReportStatus.Removed);
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
    }

    async Task Resolve(Report r, ReportStatus to)
    {
        r.Status = to;
        r.ResolvedAt = DateTime.UtcNow;
        await SocialRules.Notify(_db, NotificationType.ReportOutcome, r.ReporterId, r.ReporterId, r.Id);
    }
}
