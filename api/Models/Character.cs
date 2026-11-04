namespace api.Models;

public class Character
{
    public int Id { get; set; }
    public int AniListId { get; set; }
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
}
