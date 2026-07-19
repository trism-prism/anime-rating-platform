using System.Net;
using System.Text;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class ImporterTests
{
    class FakeHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            var anime = body.Contains("\"ANIME\"");
            // same character (id 7) appears in the anime and in the manga
            var json = $@"{{""data"":{{""Page"":{{""media"":[{{
                ""id"":{(anime ? 1 : 2)},""title"":{{""romaji"":""T"",""english"":null}},
                ""description"":""<b>Hi</b><br>there"",""genres"":[""Action"",""Drama""],""popularity"":5,
                ""characters"":{{""edges"":[{{""role"":""MAIN"",""node"":{{""id"":7,""name"":{{""full"":""Hero""}}}},
                ""voiceActors"":[{{""id"":9,""name"":{{""full"":""VA""}}}}]}}]}}}}]}}}}}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task Same_character_in_anime_and_manga_is_one_row()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(options);
        var client = new AniListClient(new HttpClient(new FakeHandler())) { DelayMs = 0 };

        var counts = await new AniListImporter(db, client).Import(1);

        Assert.Equal(1, await db.Characters.CountAsync());
        Assert.Equal(1, await db.VoiceActors.CountAsync());
        Assert.Equal(2, await db.Titles.CountAsync());
        Assert.Equal(2, await db.TitleCharacters.CountAsync());
        Assert.Equal(new ImportCounts(2, 1, 1, 2, 0, 2), counts);
        Assert.Equal("Hi\nthere", (await db.Titles.FirstAsync(t => t.Type == TitleType.Anime)).Synopsis);
    }
}
