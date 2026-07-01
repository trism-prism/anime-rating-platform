namespace api.Models;

public enum TitleType { Anime, Manga }

public class Title
{
    public int Id { get; set; }
    public int AniListId { get; set; }
    public TitleType Type { get; set; }
    public string Name { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string? Synopsis { get; set; }
    public int? Episodes { get; set; }
    public int? Chapters { get; set; }
    // comma separated, e.g. "Action,Drama"
    public string Genres { get; set; } = "";
    public int Popularity { get; set; }

    public List<TitleCharacter> Characters { get; set; } = new();
    public List<Rating> Ratings { get; set; } = new();
}
