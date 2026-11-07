using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record ClearProfileRequest(bool Bio, bool Picture);

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
        var names = await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DeletedAt != null ? AppUser.DeletedName : u.UserName!);
        // a profile report shows what is on the profile now, so the admin can judge it
        var profileIds = rows.Where(r => r.Target == ReportTarget.Profile).Select(r => r.TargetUserId).Distinct().ToList();
        var bios = await _db.Users.Where(u => profileIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Bio);
        var withAvatar = (await _db.Avatars.Where(a => profileIds.Contains(a.UserId)).Select(a => a.UserId).ToListAsync()).ToHashSet();
        return Ok(new
        {
            items = rows.Select(r => new
            {
                r.Id, target = r.Target.ToString(), r.TargetId, reason = r.Reason.ToString(), r.Note, r.Snapshot, r.CreatedAt,
                reporter = names.GetValueOrDefault(r.ReporterId), targetUser = names.GetValueOrDefault(r.TargetUserId),
                bio = r.Target == ReportTarget.Profile ? bios.GetValueOrDefault(r.TargetUserId) : null,
                hasAvatar = r.Target == ReportTarget.Profile && withAvatar.Contains(r.TargetUserId)
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
        if (r.Target == ReportTarget.Profile) return BadRequest(new { message = "Clear the bio or picture of a profile report, or dismiss it." });

        // false means it was already gone (deleted by its author): the report still counts as handled
        if (r.Target == ReportTarget.Review) await SocialRules.RemoveReviewText(_db, r.TargetId);
        else await SocialRules.RemoveReply(_db, r.TargetId);
        var same = await _db.Reports.Where(x => x.Status == ReportStatus.Open && x.Target == r.Target && x.TargetId == r.TargetId && x.TargetUserId == r.TargetUserId).ToListAsync();
        foreach (var x in same) await Resolve(x, ReportStatus.Removed);
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
    }

    // Clears the reported profile's bio and/or picture and settles every open profile report on that user (reporters are told "removed").
    [HttpPost("{id:int}/clear-profile")]
    public async Task<IActionResult> ClearProfile(int id, ClearProfileRequest body)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(x => x.Id == id && x.Status == ReportStatus.Open);
        if (r == null) return NotFound();
        if (r.Target != ReportTarget.Profile) return BadRequest(new { message = "Only profile reports can clear a bio or picture." });
        if (!body.Bio && !body.Picture) return BadRequest(new { message = "Choose the bio, the picture or both." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == r.TargetUserId);
        if (user != null && body.Bio) user.Bio = null;
        if (body.Picture) _db.Avatars.RemoveRange(await _db.Avatars.Where(a => a.UserId == r.TargetUserId).ToListAsync());
        var same = await _db.Reports.Where(x => x.Status == ReportStatus.Open && x.Target == ReportTarget.Profile && x.TargetUserId == r.TargetUserId).ToListAsync();
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
