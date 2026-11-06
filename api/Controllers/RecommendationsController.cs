using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

// "People who rated this also rated": take the users who gave this title (or any title of this series) a whole-title overall
// score of 7 or more, count what else they rated. Only aggregates leave the database: a title appears only when at least
// MinCoRaters of those users rated it, and the answer carries the count and the average, never who. Like averages and rankings
// it counts every audience, because it is an anonymous total (see ReviewRules).
// Answers are cached for 10 minutes, shared by all callers (they hold nothing personal).
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("profile")]
public class RecommendationsController : ControllerBase
{
    public const int MinCoRaters = 3, Take = 8, LikedAt = 7;
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    readonly AppDbContext _db;
    readonly IMemoryCache _cache;

    public RecommendationsController(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // a class with settable properties: Npgsql can't translate a group-by into a positional record
    class Co
    {
        public int Key { get; set; }
        public int Raters { get; set; }
        public double Average { get; set; }
    }

    [HttpGet("titles/{id:int}/also-rated")]
    public async Task<IActionResult> ForTitle(int id)
    {
        if (!await _db.Titles.AnyAsync(t => t.Id == id)) return NotFound();
        // ponytail: the cache has no size limit, one small entry per page opened; add a SizeLimit if that ever grows
        var items = await _cache.GetOrCreateAsync("also-title:" + id, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            var fans = _db.Ratings.Where(r => r.TitleId == id && r.Volume == null && r.Overall >= LikedAt).Select(r => r.UserId);
            var rows = await _db.Ratings.Where(r => r.Volume == null && r.TitleId != id && fans.Contains(r.UserId))
                .GroupBy(r => r.TitleId)
                .Select(g => new Co { Key = g.Key, Raters = g.Count(), Average = g.Average(r => r.Overall) })
                .Where(c => c.Raters >= MinCoRaters)
                .OrderByDescending(c => c.Raters).ThenByDescending(c => c.Average).ThenBy(c => c.Key).Take(Take).ToListAsync();
            var ids = rows.Select(c => c.Key).ToList();
            var names = await _db.Titles.Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Name, t.SeriesId, t.CoverUrl }).ToDictionaryAsync(t => t.Id);
            return rows.Select(c => new { titleId = c.Key, names[c.Key].Name, names[c.Key].SeriesId, names[c.Key].CoverUrl, coRaters = c.Raters, average = Math.Round(c.Average, 2) }).ToList();
        });
        return Ok(items);
    }

    [HttpGet("series/{id:int}/also-rated")]
    public async Task<IActionResult> ForSeries(int id)
    {
        if (!await _db.Series.AnyAsync(s => s.Id == id)) return NotFound();
        var items = await _cache.GetOrCreateAsync("also-series:" + id, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            var fans = _db.Ratings.Where(r => r.Title.SeriesId == id && r.Volume == null && r.Overall >= LikedAt).Select(r => r.UserId).Distinct();
            var rows = await _db.Ratings.Where(r => r.Volume == null && r.Title.SeriesId != id && fans.Contains(r.UserId))
                .GroupBy(r => r.Title.SeriesId)
                .Select(g => new Co { Key = g.Key, Raters = g.Select(r => r.UserId).Distinct().Count(), Average = g.Average(r => r.Overall) })
                .Where(c => c.Raters >= MinCoRaters)
                .OrderByDescending(c => c.Raters).ThenByDescending(c => c.Average).ThenBy(c => c.Key).Take(Take).ToListAsync();
            var ids = rows.Select(c => c.Key).ToList();
            var names = await _db.Series.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Name, s.CoverUrl }).ToDictionaryAsync(s => s.Id);
            return rows.Select(c => new { seriesId = c.Key, names[c.Key].Name, names[c.Key].CoverUrl, coRaters = c.Raters, average = Math.Round(c.Average, 2) }).ToList();
        });
        return Ok(items);
    }
}
