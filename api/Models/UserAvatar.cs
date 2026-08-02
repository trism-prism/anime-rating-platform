namespace api.Models;

// A profile picture, kept apart from AppUser so user queries never load the bytes. One row per user.
public class UserAvatar
{
    public string UserId { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public string ContentType { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // the ETag
}
