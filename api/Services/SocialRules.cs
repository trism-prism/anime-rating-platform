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
    static readonly Regex NamePattern = new(@"^[A-Za-z0-9._+@-]{1,256}\z"); // \z, not $: $ also matches before a trailing newline
    public static bool ValidUserName(string? name) => name != null && NamePattern.IsMatch(name);

    // usernames are case-insensitive: compare on Identity's normalized (upper-case) form
    public static Task<AppUser?> FindByName(AppDbContext db, string? name)
    {
        name = name?.Trim();
        if (!ValidUserName(name)) return Task.FromResult<AppUser?>(null);
        var normalized = name!.ToUpperInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);
    }

    // The account a viewer may see: null when the name is unknown OR that user blocked the viewer, so callers can't tell the
    // two apart. Both cases run this one query. Anonymous viewers (null) have no identity to block.
    public static Task<AppUser?> FindVisible(AppDbContext db, string? viewerId, string? name)
    {
        name = name?.Trim();
        if (!ValidUserName(name)) return Task.FromResult<AppUser?>(null);
        var normalized = name!.ToUpperInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized
            && !db.UserBlocks.Any(b => b.BlockerId == u.Id && b.BlockedId == viewerId));
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

    // saves; false when a row changed or vanished under it (a concurrency token or a delete matched nothing), in which case
    // nothing was written and the context is reset so the caller can look again
    public static async Task<bool> TrySave(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    // removes the rows and every notification that points at them, so the bell never shows a dead request; the caller saves
    public static async Task RemoveMany(AppDbContext db, IReadOnlyList<Friendship> rows)
    {
        var ids = rows.Select(f => (int?)f.Id).ToList();
        var dead = await db.Notifications
            .Where(n => ids.Contains(n.SubjectId) && (n.Type == NotificationType.FriendRequest || n.Type == NotificationType.FriendAccepted))
            .ToListAsync();
        db.Notifications.RemoveRange(dead);
        db.Friendships.RemoveRange(rows);
    }

    public static Task Remove(AppDbContext db, Friendship f) => RemoveMany(db, new[] { f });

    public const int RequestExpiryDays = 30;

    // Lazy expiry (no background job): pending requests older than 30 days, either direction, go with their notifications.
    // Run when a list is read or a limit is about to refuse someone, so stale requests never block a new one.
    public static async Task PruneExpired(AppDbContext db, string userId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RequestExpiryDays);
        var old = await db.Friendships
            .Where(f => f.Status == FriendStatus.Pending && f.CreatedAt < cutoff && (f.UserLowId == userId || f.UserHighId == userId))
            .Take(500).ToListAsync();
        if (old.Count == 0) return;
        await RemoveMany(db, old);
        await TrySave(db); // false: one was answered meanwhile; the next read prunes what is left
    }

    // one bucket per signed-in user (per IP when there is no token); the limiter must run after authentication
    public static RateLimitPartition<string> PerUser(HttpContext ctx, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? "ip:" + ctx.Connection.RemoteIpAddress,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window });
}
