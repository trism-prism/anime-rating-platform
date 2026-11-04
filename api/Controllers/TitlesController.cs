using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("titles")]
public class TitlesController : ControllerBase
{
    readonly AppDbContext _db;

    public TitlesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(string? search, TitleType? type, string? genre, int page = 1, int pageSize = 24)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.Titles.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(t => t.Name.ToLower().Contains(search.ToLower()));
        if (type != null) q = q.Where(t => t.Type == type);
        // genres are stored as "Action,Drama"; wrap in commas so "Action" doesn't match "Action Comedy"
        if (!string.IsNullOrWhiteSpace(genre)) q = q.Where(t => ("," + t.Genres + ",").Contains("," + genre + ","));

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(t => t.Popularity).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new
            {
                t.Id, t.Name, t.CoverUrl, t.Type, t.Genres, t.Popularity,
                AverageOverall = t.Ratings.Average(r => (double?)r.Overall),
                RatingCount = t.Ratings.Count()
            }).ToListAsync();

        var items = rows.Select(t => new
        {
            t.Id, t.Name, t.CoverUrl, type = t.Type.ToString(), genres = SplitGenres(t.Genres), t.Popularity,
            averageOverall = t.AverageOverall == null ? (double?)null : Math.Round(t.AverageOverall.Value, 2),
            t.RatingCount
        });
        return Ok(new { total, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var t = await _db.Titles.Where(x => x.Id == id)
            .Select(x => new { x.Id, x.AniListId, x.Type, x.Name, x.CoverUrl, x.Synopsis, x.Episodes, x.Chapters, x.Genres, x.Popularity })
            .FirstOrDefaultAsync();
        if (t == null) return NotFound();

        var characters = await _db.TitleCharacters.Where(tc => tc.TitleId == id)
            .Select(tc => new
            {
                tc.CharacterId,
                tc.Character.Name,
                tc.Character.ImageUrl,
                tc.Role,
                VoiceActor = tc.VoiceActor == null ? null : new { tc.VoiceActor.Id, tc.VoiceActor.Name, tc.VoiceActor.ImageUrl }
            }).ToListAsync();

        var ratings = _db.Ratings.Where(r => r.TitleId == id);
        var avg = await ratings.GroupBy(r => 1).Select(g => new
        {
            Story = g.Average(r => r.Story),
            Art = g.Average(r => r.Art),
            Characters = g.Average(r => r.Characters),
            Enjoyment = g.Average(r => r.Enjoyment),
            Overall = g.Average(r => r.Overall),
            Count = g.Count()
        }).FirstOrDefaultAsync();

        var reviews = await ratings.Where(r => r.ReviewText != null && r.ReviewText != "")
            .OrderByDescending(r => r.CreatedAt).Take(50)
            .Select(r => new
            {
                userName = r.User.UserName, r.Overall, r.Story, r.Art, r.Characters, r.Enjoyment, r.ReviewText, r.CreatedAt
            }).ToListAsync();

        return Ok(new
        {
            t.Id, t.AniListId, type = t.Type.ToString(), t.Name, t.CoverUrl, t.Synopsis, t.Episodes, t.Chapters,
            genres = SplitGenres(t.Genres), t.Popularity,
            characters,
            averages = avg == null ? null : new
            {
                story = Math.Round(avg.Story, 2), art = Math.Round(avg.Art, 2), characters = Math.Round(avg.Characters, 2),
                enjoyment = Math.Round(avg.Enjoyment, 2), overall = Math.Round(avg.Overall, 2)
            },
            ratingCount = avg?.Count ?? 0,
            reviews
        });
    }

    static string[] SplitGenres(string g) => g.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
