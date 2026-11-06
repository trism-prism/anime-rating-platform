using api.Data;
using api.Models;

namespace api.Services;

// small rules for list entries and custom lists
public static class TrackingRules
{
    public const int MaxRewatches = 1000, MaxLists = 20, MaxItems = 500, MaxListName = 60;

    // The viewer rule for custom lists, same as ReviewRules for ratings: the owner sees their own; Public: everyone, anonymous
    // included; Friends: accepted friends; Only me: the owner; a block either way hides the list. No admin bypass.
    public static IQueryable<CustomList> VisibleTo(this IQueryable<CustomList> q, AppDbContext db, string? viewer)
    {
        if (viewer == null) return q.Where(l => l.Audience == Audience.Public);
        return q.Where(l => l.UserId == viewer
            || ((l.Audience == Audience.Public
                    || (l.Audience == Audience.Friends && db.Friendships.Any(f => f.Status == FriendStatus.Accepted
                        && ((f.UserLowId == l.UserId && f.UserHighId == viewer) || (f.UserLowId == viewer && f.UserHighId == l.UserId)))))
                && !db.UserBlocks.Any(b => (b.BlockerId == l.UserId && b.BlockedId == viewer) || (b.BlockerId == viewer && b.BlockedId == l.UserId))));
    }

    // null when fine, otherwise the message. "Not in the future" allows tomorrow UTC so a user ahead of UTC can enter their today.
    public static string? CheckDates(DateOnly? started, DateOnly? finished, int rewatches)
    {
        var limit = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        if (started > limit || finished > limit) return "Dates can't be in the future.";
        if (started != null && finished != null && finished < started) return "The finish date can't be before the start date.";
        if (rewatches < 0 || rewatches > MaxRewatches) return $"Rewatch count must be between 0 and {MaxRewatches}.";
        return null;
    }
}
