namespace api.Models;

// one signed-in browser; its refresh token rotates, so the session is also the token family
public class Session
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; } // 30 days, pushed forward on every refresh
    public DateTime? RevokedAt { get; set; }
    public string UserAgent { get; set; } = "";
    public string Ip { get; set; } = "";
    public string SecurityStamp { get; set; } = ""; // the user's stamp at creation; a change ends the session
    public string CurrentTokenHash { get; set; } = "";
    public string? PreviousTokenHash { get; set; }
}
