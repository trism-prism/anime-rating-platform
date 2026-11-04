using api.Data;
using api.Models;
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
    // a title with one or two ratings would top the list, so ignore those
    public const int MinVotes = 3;

    readonly AppDbContext _db;

    public RankingsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(string category = "overall", TitleType? type = null, int take = 20)
    {
        var ratings = _db.Ratings.AsQueryable();
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

        var rows = await scores.GroupBy(s => s.TitleId)
            .Where(g => g.Count() >= MinVotes)
            .Select(g => new { TitleId = g.Key, Average = g.Average(s => s.Value), RatingCount = g.Count() })
            .Join(q, g => g.TitleId, t => t.Id, (g, t) => new { t.Id, t.Name, t.CoverUrl, t.Type, g.Average, g.RatingCount })
            .OrderByDescending(t => t.Average).ThenBy(t => t.Id)
            .Take(Math.Clamp(take, 1, 100)).ToListAsync();

        return Ok(rows.Select(t => new
        {
            t.Id, t.Name, t.CoverUrl, type = t.Type.ToString(), average = Math.Round(t.Average, 2), t.RatingCount
        }));
    }
}
