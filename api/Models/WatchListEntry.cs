namespace api.Models;

public enum WatchStatus { Planning, Watching, Completed, Dropped }

public class WatchListEntry
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public int TitleId { get; set; }
    public Title Title { get; set; } = null!;
    public WatchStatus Status { get; set; }
    public int Progress { get; set; }
    // stored and editable now; nothing shows a user's list to others yet (custom lists with audiences come later)
    public Audience Audience { get; set; } = Audience.Public;
}
