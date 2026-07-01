using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record WatchListRequest(string? Status, int Progress);

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
            .Select(w => new { titleId = w.TitleId, name = w.Title.Name, coverUrl = w.Title.CoverUrl, type = w.Title.Type.ToString(), status = w.Status.ToString(), w.Progress })
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
        if (!await _db.Titles.AnyAsync(t => t.Id == titleId)) return NotFound();

        var userId = UserId;
        var entry = await _db.WatchList.FirstOrDefaultAsync(w => w.UserId == userId && w.TitleId == titleId);
        if (entry == null) _db.WatchList.Add(entry = new WatchListEntry { UserId = userId, TitleId = titleId });
        entry.Status = status;
        entry.Progress = req.Progress;
        await _db.SaveChangesAsync();
        return Ok(new { titleId, status = status.ToString(), progress = entry.Progress });
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
