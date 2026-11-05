namespace api.Models;

// one line of the account's security history (shown under Recent activity)
public class SecurityEvent
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Ip { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime At { get; set; }
}
