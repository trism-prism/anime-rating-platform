using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record WatchListRequest(string? Status, int Progress, string? Audience = null);

[ApiController]
[Authorize]
[Route("me/watchlist")]
public class WatchListController : ControllerBase
{
    readonly AppDbContext _db;

    public WatchListController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the plain "sub" claim
    string UserId => User.FindFirst("sub")!.Value;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = UserId;
        var list = await _db.WatchList
            .Where(w => w.UserId == userId)
            .Select(w => new { titleId = w.TitleId, seriesId = w.Title.SeriesId, seriesName = w.Title.Series.Name, name = w.Title.Name, coverUrl = w.Title.CoverUrl, type = w.Title.Type.ToString(), status = w.Status.ToString(), w.Progress, audience = w.Audience.ToString() })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPut("{titleId}")]
    public async Task<IActionResult> Put(int titleId, WatchListRequest req)
    {
        // TryParse also accepts "1", so require a real name
        if (!Enum.TryParse<WatchStatus>(req.Status, true, out var status) || !Enum.IsDefined(status) || char.IsDigit(req.Status![0]))
            return BadRequest(new { message = "Status must be Planning, Watching, Completed or Dropped." });
        if (req.Progress < 0) return BadRequest(new { message = "Progress can't be negative." });
        Audience? audience = null;
        if (req.Audience != null)
        {
            if (!ReviewRules.TryAudience(req.Audience, out var a)) return BadRequest(new { message = "Audience must be Public, Friends or OnlyMe." });
            audience = a;
        }
        if (!await _db.Titles.AnyAsync(t => t.Id == titleId)) return NotFound();

        var userId = UserId;
        var entry = await _db.WatchList.FirstOrDefaultAsync(w => w.UserId == userId && w.TitleId == titleId);
        if (entry == null) _db.WatchList.Add(entry = new WatchListEntry { UserId = userId, TitleId = titleId });
        entry.Status = status;
        entry.Progress = req.Progress;
        if (audience != null) entry.Audience = audience.Value; // a new entry is Public unless told otherwise
        await _db.SaveChangesAsync();
        return Ok(new { titleId, status = status.ToString(), progress = entry.Progress, audience = entry.Audience.ToString() });
    }

    [HttpPut("{titleId}/audience")]
    [EnableRateLimiting("audience")]
    public async Task<IActionResult> SetAudience(int titleId, AudienceRequest req)
    {
        if (!ReviewRules.TryAudience(req.Audience, out var audience)) return BadRequest(new { message = "Audience must be Public, Friends or OnlyMe." });
        var userId = UserId;
        var entry = await _db.WatchList.FirstOrDefaultAsync(w => w.UserId == userId && w.TitleId == titleId);
        if (entry == null) return NotFound();
        entry.Audience = audience;
        await _db.SaveChangesAsync();
        return Ok(new { audience = audience.ToString() });
    }

    [HttpDelete("{titleId}")]
    public async Task<IActionResult> Delete(int titleId)
    {
        var userId = UserId;
        var entry = await _db.WatchList.FirstOrDefaultAsync(w => w.UserId == userId && w.TitleId == titleId);
        if (entry == null) return NotFound();
        _db.WatchList.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
