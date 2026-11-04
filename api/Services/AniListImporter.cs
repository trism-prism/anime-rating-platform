using System.Text.RegularExpressions;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public record ImportCounts(int Titles, int Characters, int VoiceActors, int Links);

public class AniListImporter
{
    readonly AppDbContext _db;
    readonly AniListClient _client;
    int _newChars, _newActors;

    public AniListImporter(AppDbContext db, AniListClient client)
    {
        _db = db;
        _client = client;
    }

    public async Task<ImportCounts> Import(int pagesPerType = 6)
    {
        // one row per AniList id, even if it shows up in both anime and manga
        var characters = new Dictionary<int, Character>();
        var actors = new Dictionary<int, VoiceActor>();
        int newTitles = 0, newLinks = 0;
        _newChars = _newActors = 0;

        foreach (var type in new[] { TitleType.Anime, TitleType.Manga })
        {
            for (var page = 1; page <= pagesPerType; page++)
            {
                var media = await _client.GetPage(type.ToString().ToUpper(), page);
                var ids = media.Select(m => m.Id).ToList();
                var titles = await _db.Titles.Where(t => ids.Contains(t.AniListId))
                    .ToDictionaryAsync(t => t.AniListId);

                foreach (var m in media)
                {
                    if (!titles.TryGetValue(m.Id, out var title))
                    {
                        title = new Title { AniListId = m.Id };
                        _db.Titles.Add(title);
                        titles[m.Id] = title;
                        newTitles++;
                    }
                    title.Type = type;
                    title.Name = m.Title.English ?? m.Title.Romaji ?? "";
                    title.CoverUrl = m.CoverImage?.Large;
                    title.Synopsis = m.Description == null ? null : Regex.Replace(Regex.Replace(m.Description, "<br\s*/?>", "
"), "<.*?>", "").Trim();
                    title.Episodes = m.Episodes;
                    title.Chapters = m.Chapters;
                    title.Genres = string.Join(",", m.Genres ?? new());
                    title.Popularity = m.Popularity;

                    foreach (var edge in m.Characters?.Edges ?? new())
                    {
                        var ch = await GetCharacter(characters, edge.Node);

                        var va = edge.VoiceActors?.FirstOrDefault();
                        VoiceActor? actor = null;
                        if (va != null)
                        {
                            actor = await GetActor(actors, va);
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
                        newLinks++;
                    }
                }
                await _db.SaveChangesAsync();
            }
        }
        return new ImportCounts(newTitles, _newChars, _newActors, newLinks);
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
