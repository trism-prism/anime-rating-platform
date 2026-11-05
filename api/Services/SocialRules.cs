using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// small helpers shared by the friends, notification and profile controllers
public static class SocialRules
{
    // lookups are exact and Identity may hold older names (generated from emails), so this only rejects junk, not the 3-30 rename rule
    static readonly Regex NamePattern = new("^[A-Za-z0-9._+@-]{1,256}$");
    public static bool ValidUserName(string? name) => name != null && NamePattern.IsMatch(name);

    // usernames are case-insensitive: compare on Identity's normalized (upper-case) form
    public static Task<AppUser?> FindByName(AppDbContext db, string? name)
    {
        name = name?.Trim();
        if (!ValidUserName(name)) return Task.FromResult<AppUser?>(null);
        var normalized = name!.ToUpperInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);
    }

    public static Task<bool> HasBlocked(AppDbContext db, string blockerId, string blockedId) =>
        db.UserBlocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId);

    public static Task<Friendship?> FindFriendship(AppDbContext db, string a, string b)
    {
        var (low, high) = Friendship.Order(a, b);
        return db.Friendships.FirstOrDefaultAsync(f => f.UserLowId == low && f.UserHighId == high);
    }

    // adds the notification to the context (the caller saves) unless the recipient turned this type off
    public static async Task Notify(AppDbContext db, NotificationType type, string recipientId, string actorId, int? subjectId)
    {
        var off = await db.NotificationPreferences.AnyAsync(p => p.UserId == recipientId && p.Type == type && !p.Enabled);
        if (!off) db.Notifications.Add(new Notification { UserId = recipientId, Type = type, ActorId = actorId, SubjectId = subjectId });
    }

    // removes the row and every notification that points at it, so the bell never shows a dead request
    public static async Task Remove(AppDbContext db, Friendship f)
    {
        var dead = await db.Notifications
            .Where(n => n.SubjectId == f.Id && (n.Type == NotificationType.FriendRequest || n.Type == NotificationType.FriendAccepted))
            .ToListAsync();
        db.Notifications.RemoveRange(dead);
        db.Friendships.Remove(f);
    }

    // one bucket per signed-in user (per IP when there is no token); the limiter must run after authentication
    public static RateLimitPartition<string> PerUser(HttpContext ctx, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? "ip:" + ctx.Connection.RemoteIpAddress,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window });
}
