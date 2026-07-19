using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record MergeRequest(int FromId, int IntoId);
public record MoveRequest(int? SeriesId);

// fixes for what the importer got wrong; moved titles are locked so the importer leaves them alone
[ApiController]
[Authorize]
[AdminOnly]
[Route("admin")]
public class AdminSeriesController : ControllerBase
{
    readonly AppDbContext _db;

    public AdminSeriesController(AppDbContext db) => _db = db;

    [HttpGet("series")]
    public async Task<IActionResult> Find(string? search)
    {
        var q = _db.Series.AsQueryable();
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s)) q = q.Match(_db, s.Length > 100 ? s[..100] : s, false);
        var items = await q.OrderByDescending(x => x.Popularity).ThenBy(x => x.Id).Take(30)
            .Select(x => new { x.Id, x.Name, Kind = x.Kind.ToString(), entries = x.Titles.Count() }).ToListAsync();
        return Ok(items);
    }

    // titles of one series with their lock state, for the move tool
    [HttpGet("series/{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var s = await _db.Series.Where(x => x.Id == id).Select(x => new { x.Id, x.Name, Kind = x.Kind.ToString() }).FirstOrDefaultAsync();
        if (s == null) return NotFound(new { message = "That series no longer exists." });
        var entries = await _db.Titles.Where(t => t.SeriesId == id).OrderBy(t => t.Position)
            .Select(t => new { t.Id, t.Name, t.Position, t.Format, t.SeriesLocked }).ToListAsync();
        return Ok(new { s.Id, s.Name, s.Kind, entries });
    }

    [HttpPost("series/merge")]
    public async Task<IActionResult> Merge(MergeRequest req)
    {
        if (req.FromId == req.IntoId) return BadRequest(new { message = "Pick two different series." });
        // held until the end: Refresh must not delete a series while we are filling it
        await using var _ = await AdvisoryLock.Wait(_db, AdvisoryLock.SeriesKey);
        var source = await _db.Series.Include(s => s.Titles).FirstOrDefaultAsync(s => s.Id == req.FromId);
        var into = await _db.Series.FirstOrDefaultAsync(s => s.Id == req.IntoId);
        if (source == null || into == null) return NotFound(new { message = "One of those series no longer exists. Search again." });
        if (source.Kind != into.Kind) return BadRequest(new { message = "Anime and manga never share a series." });

        foreach (var t in source.Titles.ToList())
        {
            t.Series = into;
            t.SeriesLocked = true;
        }
        await _db.SaveRetryingAsync(); // a rating may land on a title meanwhile
        await new SeriesAssigner(_db).Refresh(); // drops the empty series, re-positions and renames
        return NoContent();
    }

    // seriesId null = a brand-new series named after the title
    [HttpPost("titles/{id:int}/move")]
    public async Task<IActionResult> Move(int id, MoveRequest req)
    {
        await using var _ = await AdvisoryLock.Wait(_db, AdvisoryLock.SeriesKey);
        var title = await _db.Titles.FirstOrDefaultAsync(t => t.Id == id);
        if (title == null) return NotFound(new { message = "That title no longer exists." });

        Series target;
        if (req.SeriesId == null)
            _db.Series.Add(target = new Series { Kind = title.Type, Name = title.Name });
        else
        {
            var found = await _db.Series.FirstOrDefaultAsync(s => s.Id == req.SeriesId);
            if (found == null) return NotFound(new { message = "The destination series no longer exists. Search again." });
            if (found.Kind != title.Type) return BadRequest(new { message = "Anime and manga never share a series." });
            target = found;
        }

        title.Series = target;
        title.SeriesLocked = true;
        await _db.SaveRetryingAsync();
        await new SeriesAssigner(_db).Refresh();
        return Ok(new { seriesId = target.Id });
    }
}
