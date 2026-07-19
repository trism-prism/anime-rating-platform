namespace api.Models;

// our own grouping of titles (seasons, movies, volumes of one work); AniList is only one source
public class Series
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TitleType Kind { get; set; }
    public string? CoverUrl { get; set; }
    // max popularity of its entries, for ordering
    public int Popularity { get; set; }

    public List<Title> Titles { get; set; } = new();
}

// a raw AniList relation, named as AniList names it (SEQUEL, PREQUEL, SPIN_OFF, ...)
public class TitleRelation
{
    public int Id { get; set; }
    public int FromAniListId { get; set; }
    public int ToAniListId { get; set; }
    public string Type { get; set; } = "";
}
