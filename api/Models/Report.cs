namespace api.Models;

public enum ReportTarget { Review, Reply, Profile }

public enum ReportReason { Spam, Harassment, Hate, Spoiler, Other }

public enum ReportStatus { Open, Dismissed, Removed }

// Kept after its target is gone, so it has no foreign key to the review or reply; Snapshot is the text as reported.
public class Report
{
    public int Id { get; set; }
    public string ReporterId { get; set; } = "";
    public ReportTarget Target { get; set; }
    public int TargetId { get; set; } // the rating or reply id; 0 for a profile
    public string TargetUserId { get; set; } = ""; // the author of the content, or the profiled user
    public ReportReason Reason { get; set; }
    public string? Note { get; set; }
    public string Snapshot { get; set; } = "";
    public ReportStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
