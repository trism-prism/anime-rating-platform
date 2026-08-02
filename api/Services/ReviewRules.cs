using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// The viewer rule, in one place. Every route that returns a rating's score or text, a reply or a reaction goes through it:
//   the author sees their own; Public: everyone, anonymous included; Friends: accepted friends of the author;
//   OnlyMe: the author only; and if either side blocked the other the content is invisible both ways.
//   An admin may see anything (to remove it).
// Averages, counts and rankings are anonymous totals and deliberately ignore audiences.
public static class ReviewRules
{
    public const int PageSize = 10, MaxReply = 1000, MaxReview = 2000;

    public static IQueryable<Rating> VisibleTo(this IQueryable<Rating> q, AppDbContext db, string? viewer, bool admin)
    {
        if (admin) return q;
        if (viewer == null) return q.Where(r => r.Audience == Audience.Public); // nobody anonymous can have blocked or been blocked
        return q.Where(r => r.UserId == viewer
            || ((r.Audience == Audience.Public
                    || (r.Audience == Audience.Friends && db.Friendships.Any(f => f.Status == FriendStatus.Accepted
                        && ((f.UserLowId == r.UserId && f.UserHighId == viewer) || (f.UserLowId == viewer && f.UserHighId == r.UserId)))))
                && !db.UserBlocks.Any(b => (b.BlockerId == r.UserId && b.BlockedId == viewer) || (b.BlockerId == viewer && b.BlockedId == r.UserId))));
    }

    // replies have no audience of their own (they follow their review); only blocked pairs are hidden from each other
    public static IQueryable<Reply> VisibleTo(this IQueryable<Reply> q, AppDbContext db, string? viewer, bool admin)
    {
        if (admin || viewer == null) return q;
        return q.Where(x => !db.UserBlocks.Any(b => (b.BlockerId == x.UserId && b.BlockedId == viewer) || (b.BlockerId == viewer && b.BlockedId == x.UserId)));
    }

    // a review is a rating that has text
    public static IQueryable<Rating> Reviews(this IQueryable<Rating> q) => q.Where(r => r.ReviewText != null);

    // list snippet: none for a spoiler (so the list can't give it away), 140 chars max without cutting a surrogate pair
    public static string? Snippet(string? text, bool spoiler) =>
        text == null || spoiler ? null : text.Length > 140 ? text[..(char.IsHighSurrogate(text[139]) ? 139 : 140)] + "..." : text;

    // The one review the viewer may see, or null: unknown, no text, hidden by audience and blocked all look the same.
    public static Task<Rating?> FindReview(AppDbContext db, int id, string? viewer, bool admin) =>
        db.Ratings.Where(r => r.Id == id).Reviews().VisibleTo(db, viewer, admin).FirstOrDefaultAsync();

    // looked up in the database on every call, like [AdminOnly], so a demoted admin loses it at once
    public static async Task<bool> IsAdmin(AppDbContext db, string? userId) =>
        userId != null && await db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Admin"));

    // Enum.TryParse also accepts "1" and "Friends,Public", so match the names exactly (any case)
    public static bool TryName<T>(string? s, out T value) where T : struct, Enum
    {
        value = default;
        var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(s, StringComparison.OrdinalIgnoreCase));
        return name != null && Enum.TryParse(name, out value);
    }

    public static bool TryAudience(string? s, out Audience a) => TryName(s, out a);
}
