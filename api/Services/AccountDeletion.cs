using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// Removes an account and everything it owns in one transaction. Rows are removed explicitly (not left to the database cascade) so
// InMemory tests see the same result, and so the totals other people see stay right:
//  - whole-title ratings come off the title's count and sum (series averages are built from those),
//  - reactions the user left on other people's reviews come off those reviews' counters,
//  - notifications that point at removed reviews, replies, requests or reports leave other people's bells.
// Reports the user filed and reports about their content are deleted too: the content is gone, and the saved snapshot of it is
// their data. Everything else other users own is untouched.
public static class AccountDeletion
{
    const int Retries = 4;

    // false when there is no such account
    public static async Task<bool> Delete(AppDbContext db, string userId)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null; // InMemory (tests) has none
            try
            {
                var found = await RemoveAll(db, userId);
                if (tx != null) await tx.CommitAsync();
                return found;
            }
            catch (DbUpdateConcurrencyException) when (attempt < Retries)
            {
                db.ChangeTracker.Clear(); // a title total changed under us (disposing the transaction rolled back): start over
            }
        }
    }

    // ponytail: id lists go into IN (...) clauses, fine for thousands of rows; chunk them if one account ever has tens of thousands
    static async Task<bool> RemoveAll(AppDbContext db, string u)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == u);
        if (user == null) return false;
        var npgsql = db.Database.IsNpgsql();

        // ratings: title totals first
        var ratings = await db.Ratings.Where(r => r.UserId == u).ToListAsync();
        var ratingIds = ratings.Select(r => r.Id).ToList();
        var perTitle = ratings.Where(r => r.Volume == null).GroupBy(r => r.TitleId).ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Sum(r => r.Overall)));
        var titleIds = perTitle.Keys.ToList();
        foreach (var t in await db.Titles.Where(t => titleIds.Contains(t.Id)).ToListAsync())
        {
            t.RatingCount -= perTitle[t.Id].Count;
            t.RatingSum = t.RatingCount <= 0 ? 0 : t.RatingSum - perTitle[t.Id].Sum;
        }

        // replies and reactions on the user's reviews go with them; the user's own replies and reactions go too
        var replies = await db.Replies.Where(x => x.UserId == u || ratingIds.Contains(x.RatingId)).ToListAsync();
        var reactions = await db.Reactions.Where(x => x.UserId == u || ratingIds.Contains(x.RatingId)).ToListAsync();
        await SocialRules.RemoveReplyNotifications(db, replies.Select(x => x.Id).ToList());
        var ratingIdsN = ratingIds.Select(i => (int?)i).ToList();
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.Type == NotificationType.Reaction && ratingIdsN.Contains(n.SubjectId)).ToListAsync());

        // reactions the user left on other people's reviews: take them off those counters
        var onOthers = reactions.Where(x => x.UserId == u && !ratingIds.Contains(x.RatingId)).ToList();
        if (!npgsql)
            foreach (var x in onOthers)
            {
                var r = await db.Ratings.FirstAsync(y => y.Id == x.RatingId); // InMemory (the tests) has no ExecuteUpdate and is single-threaded
                r.HelpfulCount -= H(x.Kind); r.UnhelpfulCount -= Un(x.Kind); r.FunnyCount -= F(x.Kind); r.Score -= H(x.Kind) - Un(x.Kind);
            }

        // reports: filed by the user or about them, with the outcome notices they caused
        var reports = await db.Reports.Where(x => x.ReporterId == u || x.TargetUserId == u).ToListAsync();
        var reportIds = reports.Select(x => (int?)x.Id).ToList();
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.Type == NotificationType.ReportOutcome && reportIds.Contains(n.SubjectId)).ToListAsync());

        await SocialRules.RemoveMany(db, await db.Friendships.Where(f => f.UserLowId == u || f.UserHighId == u).ToListAsync());
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.UserId == u || n.ActorId == u).ToListAsync());

        var listIds = await db.CustomLists.Where(l => l.UserId == u).Select(l => l.Id).ToListAsync();
        db.CustomListItems.RemoveRange(await db.CustomListItems.Where(i => listIds.Contains(i.ListId)).ToListAsync());
        db.CustomLists.RemoveRange(await db.CustomLists.Where(l => l.UserId == u).ToListAsync());
        db.WatchList.RemoveRange(await db.WatchList.Where(w => w.UserId == u).ToListAsync());
        db.UserBlocks.RemoveRange(await db.UserBlocks.Where(b => b.BlockerId == u || b.BlockedId == u).ToListAsync());
        db.NotificationPreferences.RemoveRange(await db.NotificationPreferences.Where(p => p.UserId == u).ToListAsync());
        db.Sessions.RemoveRange(await db.Sessions.Where(s => s.UserId == u).ToListAsync());

        db.Reports.RemoveRange(reports);
        db.Reactions.RemoveRange(reactions);
        db.Replies.RemoveRange(replies);
        db.Ratings.RemoveRange(ratings);

        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == u).ToListAsync());
        db.UserLogins.RemoveRange(await db.UserLogins.Where(x => x.UserId == u).ToListAsync());
        db.UserClaims.RemoveRange(await db.UserClaims.Where(x => x.UserId == u).ToListAsync());
        db.UserTokens.RemoveRange(await db.UserTokens.Where(x => x.UserId == u).ToListAsync());
        db.Users.Remove(user);

        await db.SaveChangesAsync();

        // atomic decrements after the rows are gone, like ReviewsController.Apply; the transaction makes it all or nothing
        if (npgsql)
            foreach (var x in onOthers)
            {
                var h = H(x.Kind); var un = Un(x.Kind); var f = F(x.Kind); var id = x.RatingId;
                await db.Ratings.Where(r => r.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.HelpfulCount, r => r.HelpfulCount - h)
                    .SetProperty(r => r.UnhelpfulCount, r => r.UnhelpfulCount - un)
                    .SetProperty(r => r.FunnyCount, r => r.FunnyCount - f)
                    .SetProperty(r => r.Score, r => r.Score - h + un));
            }
        return true;
    }

    static int H(ReactionKind k) => k == ReactionKind.Helpful ? 1 : 0;
    static int Un(ReactionKind k) => k == ReactionKind.Unhelpful ? 1 : 0;
    static int F(ReactionKind k) => k == ReactionKind.Funny ? 1 : 0;
}
