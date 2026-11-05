namespace api.Models;

public enum FriendStatus { Pending, Accepted }

// one row per pair, stored in canonical order (UserLowId < UserHighId, compared byte-wise) so a pair can't exist twice
public class Friendship
{
    public int Id { get; set; }
    public string UserLowId { get; set; } = "";
    public string UserHighId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public FriendStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    public static (string Low, string High) Order(string a, string b) => string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
    public string OtherId(string me) => me == UserLowId ? UserHighId : UserLowId;
}

public class UserBlock
{
    public int Id { get; set; }
    public string BlockerId { get; set; } = "";
    public string BlockedId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// add new types at the end; they show up in the preferences list by themselves
public enum NotificationType { FriendRequest, FriendAccepted }

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = ""; // the recipient
    public NotificationType Type { get; set; }
    public string ActorId { get; set; } = "";
    public AppUser Actor { get; set; } = null!;
    public int? SubjectId { get; set; } // friend notifications: the friendship id
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

// no row means enabled
public class NotificationPreference
{
    public string UserId { get; set; } = "";
    public NotificationType Type { get; set; }
    public bool Enabled { get; set; }
}
