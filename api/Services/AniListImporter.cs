using System.Text.RegularExpressions;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public record ImportCounts(int Titles, int Characters, int VoiceActors, int Links, int Neighbours, int Series);

public class AniListImporter
{
    // neighbours (missing sequels, prequels, ...) are fetched in a few rounds, with a hard cap
    const int NeighbourRounds = 3, MaxNeighbours = 1500;

    readonly AppDbContext _db;
    readonly AniListClient _client;
    // one row per AniList id, even if it shows up in both anime and manga
    readonly Dictionary<int, Character> _characters = new();
    readonly Dictionary<int, VoiceActor> _actors = new();
    int _newTitles, _newChars, _newActors, _newLinks;

    public AniListImporter(AppDbContext db, AniListClient client)
    {
        _db = db;
        _client = client;
    }

    public async Task<ImportCounts> Import(int pagesPerType = 6)
    {
        await using var import = await AdvisoryLock.TryTake(_db, AdvisoryLock.ImportKey)
            ?? throw new InvalidOperationException("Another import is already running.");
        return await Run(pagesPerType);
    }

    async Task<ImportCounts> Run(int pagesPerType)
    {
        _characters.Clear();
        _actors.Clear();
        _newTitles = _newChars = _newActors = _newLinks = 0;

        foreach (var type in new[] { TitleType.Anime, TitleType.Manga })
            for (var page = 1; page <= pagesPerType; page++)
                await Save(await _client.GetPage(type.ToString().ToUpper(), page), type);

        var neighbours = await AddNeighbours();
        // the context has held every title since the first page; a rating made since then must not make the assigner's writes conflict
        _db.ChangeTracker.Clear();
        await new SeriesAssigner(_db).Assign();
        return new ImportCounts(_newTitles, _newChars, _newActors, _newLinks, neighbours, await _db.Series.CountAsync());
    }

    // joining relations whose target is not in the database yet, so a series is not cut in half
    async Task<int> AddNeighbours()
    {
        var tried = new HashSet<int>();
        var added = 0;
        for (var round = 0; round < NeighbourRounds && added < MaxNeighbours; round++)
        {
            var rows = await (from r in _db.TitleRelations
                              where SeriesAssigner.Joining.Contains(r.Type) && !_db.Titles.Any(t => t.AniListId == r.ToAniListId)
                              join f in _db.Titles on r.FromAniListId equals f.AniListId
                              select new { r.ToAniListId, f.Type }).ToListAsync();
            // the target must be the same kind as the title that points at it
            var wanted = rows.DistinctBy(x => x.ToAniListId).Where(x => tried.Add(x.ToAniListId))
                .Take(MaxNeighbours - added).ToDictionary(x => x.ToAniListId, x => x.Type);
            if (wanted.Count == 0) break;

            foreach (var chunk in wanted.Keys.Chunk(50))
            {
                var media = (await _client.GetByIds(chunk))
                    .Where(m => wanted.TryGetValue(m.Id, out var kind) && string.Equals(kind.ToString(), m.Type, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                await Save(media, null);
                added += media.Count;
            }
        }
        return added;
    }

    // pageType is the type asked for; neighbours have none and use the type AniList reports
    async Task Save(List<AMedia> media, TitleType? pageType)
    {
        var ids = media.Select(m => m.Id).ToList();
        var titles = await _db.Titles.Where(t => ids.Contains(t.AniListId)).ToDictionaryAsync(t => t.AniListId);
        var relations = (await _db.TitleRelations.Where(r => ids.Contains(r.FromAniListId)).ToListAsync())
            .ToLookup(r => r.FromAniListId);

        foreach (var m in media)
        {
            var type = m.Type != null ? Enum.Parse<TitleType>(m.Type, true) : pageType!.Value;
            var name = m.Title.English ?? m.Title.Romaji ?? "";
            if (!titles.TryGetValue(m.Id, out var title))
            {
                // every title needs a series; the assigner merges and renames these afterwards
                title = new Title { AniListId = m.Id, Series = new Series { Kind = type, Name = name } };
                _db.Titles.Add(title);
                titles[m.Id] = title;
                _newTitles++;
            }
            title.Type = type;
            title.Name = name;
            title.CoverUrl = m.CoverImage?.Large;
            title.Synopsis = m.Description == null ? null : Regex.Replace(Regex.Replace(m.Description, @"<br\s*/?>", "\n"), "<.*?>", "").Trim();
            title.Episodes = m.Episodes;
            title.Chapters = m.Chapters;
            title.Genres = string.Join(",", m.Genres ?? new());
            title.Popularity = m.Popularity;
            title.Format = m.Format ?? "";
            title.Year = m.StartDate?.Year;
            title.StartMonth = m.StartDate?.Month;
            title.StartDay = m.StartDate?.Day;
            title.Volumes = m.Volumes;

            // replace this title's relations with what AniList says now
            var want = (m.Relations?.Edges ?? new()).Where(e => e.RelationType != null)
                .Select(e => (To: e.Node.Id, Type: e.RelationType!)).Distinct().ToList();
            var have = relations[m.Id].ToList();
            foreach (var r in have.Where(r => !want.Contains((r.ToAniListId, r.Type)))) _db.TitleRelations.Remove(r);
            foreach (var w in want.Where(w => !have.Any(r => r.ToAniListId == w.To && r.Type == w.Type)))
                _db.TitleRelations.Add(new TitleRelation { FromAniListId = m.Id, ToAniListId = w.To, Type = w.Type });

            foreach (var edge in m.Characters?.Edges ?? new())
            {
                var ch = await GetCharacter(_characters, edge.Node);

                var va = edge.VoiceActors?.FirstOrDefault();
                VoiceActor? actor = null;
                if (va != null)
                {
                    actor = await GetActor(_actors, va);
                }

                // title or character may still be unsaved, so check by entity too
                var exists = title.Id != 0 && ch.Id != 0
                    ? await _db.TitleCharacters.AnyAsync(l => l.TitleId == title.Id && l.CharacterId == ch.Id)
                    : false;
                exists = exists || title.Characters.Any(l => l.Character == ch);
                if (exists) continue;

                title.Characters.Add(new TitleCharacter
                {
                    Character = ch,
                    Role = edge.Role ?? "",
                    VoiceActor = actor
                });
                _newLinks++;
            }
        }
        await _db.SaveRetryingAsync();
    }

    async Task<Character> GetCharacter(Dictionary<int, Character> seen, ACharacterNode n)
    {
        if (seen.TryGetValue(n.Id, out var c)) return c;
        c = await _db.Characters.FirstOrDefaultAsync(x => x.AniListId == n.Id);
        if (c == null)
        {
            c = new Character { AniListId = n.Id };
            _db.Characters.Add(c);
            _newChars++;
        }
        c.Name = n.Name.Full ?? "";
        c.ImageUrl = n.Image?.Large;
        seen[n.Id] = c;
        return c;
    }

    async Task<VoiceActor> GetActor(Dictionary<int, VoiceActor> seen, AVoiceActor v)
    {
        if (seen.TryGetValue(v.Id, out var a)) return a;
        a = await _db.VoiceActors.FirstOrDefaultAsync(x => x.AniListId == v.Id);
        if (a == null)
        {
            a = new VoiceActor { AniListId = v.Id };
            _db.VoiceActors.Add(a);
            _newActors++;
        }
        a.Name = v.Name.Full ?? "";
        a.ImageUrl = v.Image?.Large;
        seen[v.Id] = a;
        return a;
    }
}
