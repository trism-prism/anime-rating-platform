namespace api.Models;

// a user-named list of titles and series; Audience uses the same viewer rule as reviews
public class CustomList
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public string Name { get; set; } = "";
    public Audience Audience { get; set; } = Audience.Public;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CustomListItem> Items { get; set; } = new();
}

// exactly one of TitleId and SeriesId is set (check constraint)
public class CustomListItem
{
    public int Id { get; set; }
    public int ListId { get; set; }
    public CustomList List { get; set; } = null!;
    public int? TitleId { get; set; }
    public Title? Title { get; set; }
    public int? SeriesId { get; set; }
    public Series? Series { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
