namespace api.Models;

public class Rating
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public int TitleId { get; set; }
    public Title Title { get; set; } = null!;
    // null = the whole title; 1..Volumes for a manga volume
    public int? Volume { get; set; }
    // each 1-10
    public int Story { get; set; }
    public int Art { get; set; }
    public int Characters { get; set; }
    public int Enjoyment { get; set; }
    // average of the four, set on save
    public double Overall { get; set; }
    public string? ReviewText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
