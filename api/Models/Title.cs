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

    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    // order inside the series, 1..n
    public int Position { get; set; }
    // AniList format: TV, TV_SHORT, MOVIE, SPECIAL, OVA, ONA, MUSIC, MANGA, NOVEL, ONE_SHOT
    public string Format { get; set; } = "";
    public int? Year { get; set; }
    public int? StartMonth { get; set; }
    public int? StartDay { get; set; }
    // manga volume count from the source; null = unknown or ongoing
    public int? Volumes { get; set; }
    // an admin placed it; the importer never moves it
    public bool SeriesLocked { get; set; }
    // stored totals of every rating (whole title and volumes); concurrency tokens, see RatingsController
    public int RatingCount { get; set; }
    public double RatingSum { get; set; }

    public List<TitleCharacter> Characters { get; set; } = new();
    public List<Rating> Ratings { get; set; } = new();
}
