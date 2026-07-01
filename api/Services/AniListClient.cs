using System.Net;
using System.Text;
using System.Text.Json;

namespace api.Services;

public record AName(string? Full);
public record AImage(string? Large);
public record ATitle(string? Romaji, string? English);
public record AVoiceActor(int Id, AName Name, AImage? Image);
public record ACharacterNode(int Id, AName Name, AImage? Image);
public record ACharacterEdge(string? Role, ACharacterNode Node, List<AVoiceActor>? VoiceActors);
public record ACharacters(List<ACharacterEdge>? Edges);
public record ATrending(int Id, ATitle Title, AImage? CoverImage, int Trending, int Popularity);
public record AMedia(int Id, ATitle Title, AImage? CoverImage, string? Description, int? Episodes,
    int? Chapters, List<string>? Genres, int Popularity, ACharacters? Characters);

public class AniListClient
{
    const string Query = @"query ($page: Int, $type: MediaType) {
  Page(page: $page, perPage: 50) {
    media(type: $type, sort: POPULARITY_DESC) {
      id title { romaji english } coverImage { large } description episodes chapters genres popularity
      characters(perPage: 10, sort: ROLE) {
        edges { role node { id name { full } image { large } }
          voiceActors(language: JAPANESE) { id name { full } image { large } } }
      }
    }
  }
}";

    const string TrendingQuery = @"query ($type: MediaType) {
  Page(page: 1, perPage: 20) {
    media(type: $type, sort: TRENDING_DESC) { id title { romaji english } coverImage { large } trending popularity }
  }
}";

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    readonly HttpClient _http;

    // pause between calls, AniList rate limit is about 90/min
    public int DelayMs { get; set; } = 700;

    public AniListClient(HttpClient http) => _http = http;

    public async Task<List<AMedia>> GetPage(string type, int page)
    {
        var body = JsonSerializer.Serialize(new { query = Query, variables = new { page, type } });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync("https://graphql.anilist.co", content);

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = res.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                await Task.Delay(TimeSpan.FromSeconds(wait));
                continue;
            }

            res.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            await Task.Delay(DelayMs);
            return doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media")
                .Deserialize<List<AMedia>>(Json)!;
        }
        throw new Exception("AniList kept answering 429");
    }

    public async Task<List<ATrending>> GetTrending(string type)
    {
        var body = JsonSerializer.Serialize(new { query = TrendingQuery, variables = new { type } });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync("https://graphql.anilist.co", content);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media")
            .Deserialize<List<ATrending>>(Json)!;
    }
}
