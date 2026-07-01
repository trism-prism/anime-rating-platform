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
            .Select(t => new
            {
                t.Id, t.AniListId, t.Name, t.CoverUrl, t.Type,
                AverageOverall = t.Ratings.Average(r => (double?)r.Overall)
            }).ToListAsync();

        // keep AniList's order, skip what we don't have
        var items = rows.OrderBy(t => ids.IndexOf(t.AniListId)).Select(t => new
        {
            t.Id, t.Name, t.CoverUrl, type = t.Type.ToString(),
            averageOverall = t.AverageOverall == null ? (double?)null : Math.Round(t.AverageOverall.Value, 2)
        });
        return Ok(items);
    }
}
