using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("trending")]
public class TrendingController : ControllerBase
{
    readonly AppDbContext _db;
    readonly AniListClient _anilist;
    readonly IMemoryCache _cache;

    public TrendingController(AppDbContext db, AniListClient anilist, IMemoryCache cache)
    {
        _db = db;
        _anilist = anilist;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> Get(TitleType type = TitleType.Anime)
    {
        var key = "trending-" + type;
        var staleKey = key + "-stale"; // last good answer, never expires, used when AniList is down

        if (!_cache.TryGetValue(key, out List<int>? ids))
        {
            try
            {
                var media = await _anilist.GetTrending(type.ToString().ToUpper());
                ids = media.Select(m => m.Id).ToList();
                _cache.Set(key, ids, TimeSpan.FromMinutes(10));
                _cache.Set(staleKey, ids);
            }
            catch (Exception)
            {
                _cache.TryGetValue(staleKey, out ids);
            }
        }
        if (ids == null || ids.Count == 0) return Ok(Array.Empty<object>());

        var rows = await _db.Titles.Where(t => t.Type == type && ids.Contains(t.AniListId))
            .Select(t => new { t.AniListId, t.SeriesId }).ToListAsync();

        // keep AniList's order, skip what we don't have, one entry per series
        var seriesIds = rows.OrderBy(t => ids.IndexOf(t.AniListId)).Select(t => t.SeriesId).Distinct().ToList();
        var series = await _db.Series.Where(s => seriesIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.CoverUrl, s.Kind, Entries = s.Titles.Select(t => new { t.RatingCount, t.RatingSum }).ToList() })
            .ToListAsync();

        var items = seriesIds.Select(id => series.First(s => s.Id == id)).Select(s => new
        {
            s.Id, s.Name, s.CoverUrl, type = s.Kind.ToString(),
            averageOverall = RatingStats.SeriesAverage(s.Entries.Select(e => (e.RatingCount, e.RatingSum)))
        });
        return Ok(items);
    }
}
