using System.Text.RegularExpressions;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public static class SeriesNames
{
    // trailing "Season 2", "2nd Season", "Final Season", "Part 2", "Cour 2", or ": II" / "- 2"
    static readonly Regex Tail = new(
        @"(?:[\s:\-–]+(?:the\s+)?(?:final\s+season|season\s*\d+|\d+(?:st|nd|rd|th)\s+(?:season|cour)|part\s*\d+|cour\s*\d+)|\s*[:\-–]\s*(?:\d+|[ivx]+))\s*$",
        RegexOptions.IgnoreCase);

    public static string Strip(string name)
    {
        var s = name.Trim();
        while (true)
        {
            var next = Tail.Replace(s, "").TrimEnd(' ', ':', '-', '–');
            if (next == s || next == "") return s;
            s = next;
        }
    }
}

public static class SaveRetry
{
    // a rating can land between our read and write; RatingCount/RatingSum are concurrency tokens, so reload
    // the tokens from the database and write again. Our own changes are kept; rows deleted meanwhile are dropped.
    public static async Task SaveRetryingAsync(this DbContext db, int retries = 4)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await db.SaveChangesAsync(); return; }
            catch (DbUpdateConcurrencyException ex) when (attempt < retries)
            {
                foreach (var e in ex.Entries)
                {
                    var current = await e.GetDatabaseValuesAsync();
                    if (current == null) { e.State = EntityState.Detached; continue; }
                    foreach (var p in e.Properties)
                    {
                        var latest = current[p.Metadata];
                        // untouched columns (the rating tokens) take the database value so they are not written back stale
                        if (!p.IsModified) p.CurrentValue = latest;
                        p.OriginalValue = latest;
                    }
                }
            }
        }
    }
}

public class SeriesAssigner
{
    // relation types that put two titles of the same kind in one series; anything else only shows as "Related"
    public static readonly string[] Joining = { "PREQUEL", "SEQUEL", "PARENT", "SIDE_STORY", "SUMMARY" };

    readonly AppDbContext _db;

    public SeriesAssigner(AppDbContext db) => _db = db;

    // group unlocked titles into series by the joining relations, then fix names and order
    public async Task Assign()
    {
        var titles = await _db.Titles.ToListAsync();
        var series = await _db.Series.ToDictionaryAsync(s => s.Id);
        var relations = await _db.TitleRelations.Where(r => Joining.Contains(r.Type)).ToListAsync();

        var byAniList = titles.ToDictionary(t => t.AniListId);
        var parent = titles.ToDictionary(t => t.Id, t => t.Id);
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        foreach (var r in relations)
            if (byAniList.TryGetValue(r.FromAniListId, out var a) && byAniList.TryGetValue(r.ToAniListId, out var b)
                && a.Type == b.Type && !a.SeriesLocked && !b.SeriesLocked)
                parent[Find(a.Id)] = Find(b.Id);

        // lowest ids first, so a series id is never taken from an older one
        var claimed = new HashSet<int>();
        var moves = new List<(Series Series, int[] Ids)>();
        var sql = _db.Database.IsNpgsql();
        foreach (var group in titles.Where(t => !t.SeriesLocked).GroupBy(t => Find(t.Id)).OrderBy(g => g.Min(t => t.SeriesId)))
        {
            var id = group.Select(t => t.SeriesId).Distinct().OrderBy(i => i).FirstOrDefault(i => i != 0 && !claimed.Contains(i));
            Series s;
            if (id != 0) { s = series[id]; claimed.Add(id); }
            else _db.Series.Add(s = new Series { Kind = group.First().Type });

            var moving = group.Where(t => t.Series != s).ToList();
            if (sql) moves.Add((s, moving.Select(t => t.Id).ToArray()));
            else foreach (var t in moving) t.Series = s;
        }
        await _db.SaveRetryingAsync(); // new series rows (and, on InMemory, the moves)

        // the write is guarded in the database: a title an admin locked since we read it is skipped
        foreach (var (s, ids) in moves)
            if (ids.Length > 0)
                await _db.Titles.Where(t => ids.Contains(t.Id) && !t.SeriesLocked)
                    .ExecuteUpdateAsync(x => x.SetProperty(t => t.SeriesId, s.Id));
        _db.ChangeTracker.Clear(); // drop snapshots made stale by the writes above and by concurrent ratings
        await Refresh();
    }

    // positions, name, cover and popularity of every series; drops series with no titles
    public async Task Refresh()
    {
        foreach (var s in await _db.Series.Include(s => s.Titles).ToListAsync())
        {
            // a title an admin moved in since the list was read must not be cascade-deleted with its series
            if (s.Titles.Count == 0)
            {
                if (_db.Database.IsNpgsql()) await _db.Series.Where(x => x.Id == s.Id && !x.Titles.Any()).ExecuteDeleteAsync();
                else _db.Series.Remove(s);
                continue;
            }

            var ordered = s.Titles.OrderBy(t => t.Year ?? int.MaxValue).ThenBy(t => t.StartMonth ?? 0)
                .ThenBy(t => t.StartDay ?? 0).ThenBy(t => t.Id).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;

            s.Name = SeriesNames.Strip(ordered[0].Name);
            s.Kind = ordered[0].Type;
            s.CoverUrl = ordered.Select(t => t.CoverUrl).FirstOrDefault(c => c != null);
            s.Popularity = ordered.Max(t => t.Popularity);
        }
        await _db.SaveRetryingAsync();
    }
}
