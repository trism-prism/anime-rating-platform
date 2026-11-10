using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

// plain properties, not a constructor, so Npgsql can translate GroupBy on it
class Score { public int TitleId { get; set; } public double Value { get; set; } }

[ApiController]
[AllowAnonymous]
[Route("rankings")]
public class RankingsController : ControllerBase
{
    // a series with one or two ratings would top the list, so ignore those (the count is the scoped one under ?scope=friends|me)
    public const int MinVotes = 3;

    readonly AppDbContext _db;

    public RankingsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(string category = "overall", TitleType? type = null, int take = 20, string? scope = null)
    {
        var sc = RatingScopes.Resolve(this, scope);
        if (sc.Error != null) return sc.Error;
        // whole-title ratings only: one account rating three volumes must not meet MinVotes
        var ratings = _db.Ratings.Where(r => r.Volume == null).InScope(_db, sc);
        IQueryable<Score>? scores = category.ToLower() switch
        {
            "overall" => ratings.Select(r => new Score { TitleId = r.TitleId, Value = r.Overall }),
            "story" => ratings.Select(r => new Score { TitleId = r.TitleId, Value = r.Story }),
            "art" => ratings.Select(r => new Score { TitleId = r.TitleId, Value = r.Art }),
            "characters" => ratings.Select(r => new Score { TitleId = r.TitleId, Value = r.Characters }),
            "enjoyment" => ratings.Select(r => new Score { TitleId = r.TitleId, Value = r.Enjoyment }),
            _ => null
        };
        if (scores == null) return BadRequest("category must be overall, story, art, characters or enjoyment");

        var q = _db.Titles.AsQueryable();
        if (type != null) q = q.Where(t => t.Type == type);

        // average per title in the database, then a series is the average of its titles' averages
        var perTitle = await scores.GroupBy(s => s.TitleId)
            .Select(g => new { TitleId = g.Key, Average = g.Average(s => s.Value), Count = g.Count() })
            .Join(q, g => g.TitleId, t => t.Id, (g, t) => new { t.SeriesId, g.Average, g.Count })
            .ToListAsync();

        var top = perTitle.GroupBy(x => x.SeriesId)
            .Select(g => new { SeriesId = g.Key, Average = g.Average(x => x.Average), RatingCount = g.Sum(x => x.Count) })
            .Where(x => x.RatingCount >= MinVotes)
            .OrderByDescending(x => x.Average).ThenBy(x => x.SeriesId)
            .Take(Math.Clamp(take, 1, 100)).ToList();

        var ids = top.Select(x => x.SeriesId).ToList();
        var series = await _db.Series.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);

        return Ok(top.Select(x => new
        {
            Id = x.SeriesId, series[x.SeriesId].Name, series[x.SeriesId].CoverUrl, type = series[x.SeriesId].Kind.ToString(),
            average = Math.Round(x.Average, 2), x.RatingCount
        }));
    }
}
