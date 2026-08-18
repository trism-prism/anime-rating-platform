using System.Text;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[AllowAnonymous]
public class SeriesController : ControllerBase
{
    // anime <-> manga links shown as "also as manga/anime" instead of "related"
    static readonly string[] CrossTypes = { "ADAPTATION", "SOURCE" };

    readonly AppDbContext _db;

    public SeriesController(AppDbContext db) => _db = db;

    // sort: popular (most ratings, then AniList popularity), top (best average, rated only), newest (latest year), az.
    // Every order ends in Id, so equal rows never swap between pages. The cursor is base64url of the offset: the sort
    // keys are computed per series (ratings, average, year), so a keyset would need them stored.
    // ponytail: offset paging over computed keys; store them on Series and use a keyset if the table grows 100x.
    // Accepted limits: a rating saved between two pages can move a series, so a row may repeat (the client drops repeats) or be skipped;
    // A to Z follows the database collation (Postgres) and plain ordinal order on InMemory.
    // total is counted on the first page only (cursor == null); the client keeps it for later pages.
    [HttpGet("series")]
    public async Task<IActionResult> List(string? search, TitleType? type, string? genre, string? cursor, string? sort, int take = 24, string? scope = null)
    {
        var sc = RatingScopes.Resolve(this, scope);
        if (sc.Error != null) return sc.Error;
        take = Math.Clamp(take, 1, 100);
        var skip = 0;
        if (cursor != null && !TryDecode(cursor, out skip)) return BadRequest("Bad cursor.");
        sort = sort?.ToLowerInvariant();
        var q = _db.Series.AsQueryable();
        if (type != null) q = q.Where(s => s.Kind == type);
        // genres are stored as "Action,Drama"; wrap in commas so "Action" doesn't match "Action Comedy"
        if (!string.IsNullOrWhiteSpace(genre)) q = q.Where(s => s.Titles.Any(t => ("," + t.Genres + ",").Contains("," + genre + ",")));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Match(_db, Clean(search), false);

        // ?scope=friends|me: the per-title totals come from just those ratings instead of the stored ones (below)
        var mine = sc.Scoped ? await _db.Ratings.InScope(_db, sc).TitleTotals() : null;
        if (sort == "top" && mine == null) q = q.Where(s => s.Titles.Any(t => t.RatingCount > 0));

        int? total = cursor == null ? await q.CountAsync() : null;

        var keyed = q.Select(s => new
        {
            s.Id, s.Name, s.Kind, s.CoverUrl, s.Popularity,
            Ratings = s.Titles.Sum(t => t.RatingCount),
            Avg = s.Titles.Where(t => t.RatingCount > 0).Average(t => (double?)(t.RatingSum / t.RatingCount)) ?? 0,
            Year = s.Titles.Max(t => t.Year) ?? 0, // 0 so unknown years sort last (Postgres puts NULL first when descending)
            Entries = s.Titles.Select(t => new { t.Id, t.RatingCount, t.RatingSum, t.Genres }).ToList()
        });

        // Scoped popular / top: the series with scoped ratings are ordered here (few: only what the viewer or their friends rated);
        // popular then continues with the unrated ones by popularity, so the order is still total and ends in id.
        List<int>? pick = null;
        if (mine != null && sort != "newest" && sort != "az")
        {
            var top = sort == "top";
            var titleSeries = await _db.Titles.Where(t => mine.Keys.Contains(t.Id)).Select(t => new { t.Id, t.SeriesId }).ToListAsync();
            var bySeries = titleSeries.GroupBy(t => t.SeriesId).ToDictionary(g => g.Key, g => (
                N: g.Sum(t => mine[t.Id].N), Avg: g.Average(t => mine[t.Id].Sum / mine[t.Id].N)));
            var allowed = bySeries.Keys.ToList();
            var rated = (await q.Where(s => allowed.Contains(s.Id)).Select(s => new { s.Id, s.Popularity }).ToListAsync())
                .Select(s => (s.Id, s.Popularity, bySeries[s.Id].N, bySeries[s.Id].Avg)).ToList();
            if (top) total = cursor == null ? rated.Count : (int?)null;
            pick = (top ? rated.OrderByDescending(s => s.Avg).ThenByDescending(s => s.N).ThenByDescending(s => s.Id)
                        : rated.OrderByDescending(s => s.N).ThenByDescending(s => s.Popularity).ThenByDescending(s => s.Id))
                .Select(s => s.Id).Skip(skip).Take(take + 1).ToList();
            if (!top && pick.Count < take + 1)
                pick.AddRange(await q.Where(s => !allowed.Contains(s.Id)).OrderByDescending(s => s.Popularity).ThenByDescending(s => s.Id)
                    .Select(s => s.Id).Skip(Math.Max(0, skip - rated.Count)).Take(take + 1 - pick.Count).ToListAsync());
        }
        var ordered = pick != null ? keyed.Where(s => pick.Contains(s.Id)) : sort switch
        {
            "top" => keyed.OrderByDescending(s => s.Avg).ThenByDescending(s => s.Ratings).ThenByDescending(s => s.Id),
            "newest" => keyed.OrderByDescending(s => s.Year).ThenByDescending(s => s.Popularity).ThenByDescending(s => s.Id),
            "az" => keyed.OrderBy(s => s.Name).ThenBy(s => s.Id),
            _ => keyed.OrderByDescending(s => s.Ratings).ThenByDescending(s => s.Popularity).ThenByDescending(s => s.Id),
        };

        var rows = await ordered.Skip(pick != null ? 0 : skip).Take(take + 1).ToListAsync();
        if (pick != null) rows = rows.OrderBy(r => pick.IndexOf(r.Id)).ToList();
        var more = rows.Count > take;
        rows = rows.Take(take).ToList();
        var items = rows.Select(s => new
        {
            s.Id, s.Name, type = s.Kind.ToString(), s.CoverUrl, s.Popularity,
            entryCount = s.Entries.Count,
            average = RatingStats.SeriesAverage(s.Entries.Select(e => Totals(mine, e.Id, e.RatingCount, e.RatingSum))),
            ratingCount = s.Entries.Sum(e => Totals(mine, e.Id, e.RatingCount, e.RatingSum).Count),
            year = s.Year == 0 ? (int?)null : s.Year,
            genres = s.Entries.SelectMany(e => e.Genres.Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToList()
        });
        return Ok(new { items, total, nextCursor = more ? Encode(skip + take) : null });
    }

    [HttpGet("series/{id:int}")]
    public async Task<IActionResult> Get(int id, string? scope = null)
    {
        var sc = RatingScopes.Resolve(this, scope);
        if (sc.Error != null) return sc.Error;
        var s = await _db.Series.Where(x => x.Id == id).Select(x => new { x.Id, x.Name, x.Kind, x.CoverUrl }).FirstOrDefaultAsync();
        if (s == null) return NotFound();

        var entries = await _db.Titles.Where(t => t.SeriesId == id).OrderBy(t => t.Position)
            .Select(t => new
            {
                t.Id, t.AniListId, t.Name, t.Format, t.Year, t.Position, t.Volumes, t.Episodes, t.Chapters, t.CoverUrl,
                t.RatingCount, t.RatingSum, t.Genres
            }).ToListAsync();
        if (sc.Scoped)
        {
            var mine = await _db.Ratings.Where(r => r.Title.SeriesId == id).InScope(_db, sc).TitleTotals();
            entries = entries.Select(e => { var (n, sum) = Totals(mine, e.Id, 0, 0); return e with { RatingCount = n, RatingSum = sum }; }).ToList();
        }
        // only the lead entry's text is needed, so it is read apart from the list
        var synopsis = await _db.Titles.Where(t => t.SeriesId == id).OrderBy(t => t.Position).Select(t => t.Synopsis).FirstOrDefaultAsync();

        // relations that did not join the series, when the target is in our database
        var aniIds = entries.Select(e => e.AniListId).ToList();
        var links = await (from r in _db.TitleRelations
                           where aniIds.Contains(r.FromAniListId) && !SeriesAssigner.Joining.Contains(r.Type)
                           join t in _db.Titles on r.ToAniListId equals t.AniListId
                           where t.SeriesId != id
                           select new { r.Type, TitleId = t.Id, t.Name, t.SeriesId, SeriesName = t.Series.Name, Kind = t.Type })
            .ToListAsync();

        var crossLinks = links.Where(l => CrossTypes.Contains(l.Type) && l.Kind != s.Kind)
            .DistinctBy(l => l.SeriesId).Select(l => new { seriesId = l.SeriesId, name = l.SeriesName, type = l.Kind.ToString() });
        var related = links.Where(l => !(CrossTypes.Contains(l.Type) && l.Kind != s.Kind))
            .GroupBy(l => l.Type).Select(g => new
            {
                type = g.Key,
                items = g.DistinctBy(l => l.TitleId).Select(l => new { l.TitleId, l.Name, l.SeriesId, l.SeriesName })
            });

        return Ok(new
        {
            s.Id, s.Name, type = s.Kind.ToString(), s.CoverUrl,
            synopsis, // the lead entry's
            format = entries.FirstOrDefault()?.Format,
            genres = entries.SelectMany(e => e.Genres.Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToList(),
            average = RatingStats.SeriesAverage(entries.Select(e => (e.RatingCount, e.RatingSum))),
            ratingCount = entries.Sum(e => e.RatingCount),
            entries = entries.Select(e => new
            {
                e.Id, e.Name, e.Format, e.Year, e.Position, e.Volumes, e.Episodes, e.Chapters, e.CoverUrl,
                average = e.RatingCount == 0 ? (double?)null : Math.Round(e.RatingSum / e.RatingCount, 2),
                ratingCount = e.RatingCount
            }),
            related,
            crossLinks
        });
    }

    // typeahead: up to 8 series, names starting with the text first, then names containing it
    [HttpGet("search/suggest")]
    public async Task<IActionResult> Suggest(string? q)
    {
        var text = Clean(q);
        if (text.Length < 2) return Ok(Array.Empty<object>());

        var found = await _db.Series.Match(_db, text, true).OrderByDescending(s => s.Popularity).ThenBy(s => s.Id)
            .Take(8).Select(s => new { s.Id, s.Name, s.Kind }).ToListAsync();
        if (found.Count < 8)
        {
            var have = found.Select(f => f.Id).ToList();
            found.AddRange(await _db.Series.Match(_db, text, false).Where(s => !have.Contains(s.Id))
                .OrderByDescending(s => s.Popularity).ThenBy(s => s.Id)
                .Take(8 - found.Count).Select(s => new { s.Id, s.Name, s.Kind }).ToListAsync());
        }
        return Ok(found.Select(f => new { f.Id, f.Name, kind = f.Kind.ToString() }));
    }

    // an entry's (count, sum): the stored totals, or under a scope the totals of the scoped ratings (none = 0)
    static (int Count, double Sum) Totals(Dictionary<int, (int N, double Sum)>? scoped, int titleId, int count, double sum) =>
        scoped == null ? (count, sum) : scoped.TryGetValue(titleId, out var t) ? (t.N, t.Sum) : (0, 0);

    static string Clean(string? s)
    {
        s = s?.Trim() ?? "";
        return s.Length > 100 ? s[..100] : s;
    }

    static string Encode(int offset) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(offset.ToString()));

    static bool TryDecode(string cursor, out int offset)
    {
        offset = 0;
        try
        {
            return int.TryParse(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)), out offset) && offset >= 0;
        }
        catch (FormatException) { return false; }
    }
}
