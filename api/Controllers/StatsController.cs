using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

// Your own numbers only, from your list entries and ratings. A user's list is small, so the rows are loaded and counted in
// memory (plain LINQ) rather than grouped in SQL.
[ApiController]
[Authorize]
[Route("me/stats")]
[EnableRateLimiting("profile")]
public class StatsController : ControllerBase
{
    readonly AppDbContext _db;

    public StatsController(AppDbContext db) => _db = db;

    string Me => User.FindFirst("sub")!.Value;

    record Entry(WatchStatus Status, TitleType Type, string Format, string Genres, int? Episodes, int Progress, DateOnly? FinishedOn);
    record Score(int TitleId, string TitleName, int? Volume, double Overall, DateTime CreatedAt);

    Task<List<Entry>> Entries(string me) => _db.WatchList.Where(w => w.UserId == me)
        .Select(w => new Entry(w.Status, w.Title.Type, w.Title.Format, w.Title.Genres, w.Title.Episodes, w.Progress, w.FinishedOn)).ToListAsync();

    Task<List<Score>> Scores(string me) => _db.Ratings.Where(r => r.UserId == me)
        .Select(r => new Score(r.TitleId, r.Title.Name, r.Volume, r.Overall, r.CreatedAt)).ToListAsync();

    // a finished anime counts all its episodes even if progress was never filled in
    static int Done(Entry e) => e.Status == WatchStatus.Completed ? Math.Max(e.Progress, e.Episodes ?? 0) : e.Progress;

    static int Episodes(IEnumerable<Entry> es) => es.Where(e => e.Type == TitleType.Anime).Sum(Done);

    static double? Average(IEnumerable<Score> rs) => rs.Any() ? Math.Round(rs.Average(r => r.Overall), 2) : null;

    // score 1..10 -> index 0..9
    static int[] Distribution(IEnumerable<Score> rs)
    {
        var d = new int[10];
        foreach (var r in rs) d[Math.Clamp((int)Math.Round(r.Overall, MidpointRounding.AwayFromZero), 1, 10) - 1]++;
        return d;
    }

    static object[] TopGenres(IEnumerable<Entry> es, int n) => es
        .SelectMany(e => e.Genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .GroupBy(g => g).Select(g => new { name = g.Key, count = g.Count() })
        .OrderByDescending(g => g.count).ThenBy(g => g.name).Take(n).Cast<object>().ToArray();

    [HttpGet]
    public async Task<IActionResult> All()
    {
        var me = Me;
        var entries = await Entries(me);
        var scores = await Scores(me);
        var whole = scores.Where(s => s.Volume == null).ToList(); // the average and spread are of whole-title ratings
        return Ok(new
        {
            statusCounts = Enum.GetValues<WatchStatus>().ToDictionary(s => s.ToString(), s => entries.Count(e => e.Status == s)),
            ratingCount = whole.Count,
            averageScore = Average(whole),
            distribution = Distribution(whole),
            genres = TopGenres(entries, 10),
            formats = entries.GroupBy(e => e.Format == "" ? "Unknown" : e.Format).Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(g => g.count).ThenBy(g => g.name),
            episodes = Episodes(entries),
            chapters = entries.Where(e => e.Type == TitleType.Manga).Sum(Done),
            volumesRated = scores.Count(s => s.Volume != null)
        });
    }

    // year in review: what you finished and rated in that calendar year
    [HttpGet("{year:int}")]
    public async Task<IActionResult> Year(int year)
    {
        if (year < 1970 || year > DateTime.UtcNow.Year + 1) return NotFound();
        var me = Me;
        var finished = (await Entries(me)).Where(e => e.FinishedOn?.Year == year).ToList();
        var rated = (await Scores(me)).Where(s => s.CreatedAt.Year == year && s.Volume == null).ToList();
        var byMonth = new int[12];
        foreach (var e in finished) byMonth[e.FinishedOn!.Value.Month - 1]++;
        return Ok(new
        {
            year,
            finished = finished.Count,
            episodes = Episodes(finished),
            chapters = finished.Where(e => e.Type == TitleType.Manga).Sum(Done),
            byMonth,
            genres = TopGenres(finished, 5),
            ratingCount = rated.Count,
            averageScore = Average(rated),
            topRated = rated.OrderByDescending(s => s.Overall).ThenBy(s => s.TitleName).Take(5)
                .Select(s => new { s.TitleId, name = s.TitleName, s.Overall })
        });
    }
}
