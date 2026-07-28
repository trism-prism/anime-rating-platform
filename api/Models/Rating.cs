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
    // governs both the score and the review text; new ratings are public, rows from before audiences were migrated to OnlyMe
    public Audience Audience { get; set; } = Audience.Public;
    // the client collapses the whole review behind a button; the API still sends the text
    public bool Spoiler { get; set; }
    // reaction totals, changed only by atomic increments in the same transaction as the reaction row (see ReactionsController);
    // Score = HelpfulCount - UnhelpfulCount is what "helpful" sorting uses, stored so sort and keyset cursor agree
    public int HelpfulCount { get; set; }
    public int UnhelpfulCount { get; set; }
    public int FunnyCount { get; set; }
    public int Score { get; set; }
}
