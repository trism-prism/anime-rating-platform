using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public static class AccountDeletion
{
    // false when there is no such account (or it is already deleted)
    public static async Task<bool> Delete(AppDbContext db, string userId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await Anonymize(db, userId); }
            catch (DbUpdateConcurrencyException) when (attempt < 4)
            {
                db.ChangeTracker.Clear(); // a friend request or report changed under us (the transaction rolled back): start over
            }
        }
    }

    static async Task<bool> Anonymize(AppDbContext db, string u)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null; // InMemory (tests) has none
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == u && x.DeletedAt == null);
        if (user == null) return false;

        await SocialRules.RemoveMany(db, await db.Friendships.Where(f => f.UserLowId == u || f.UserHighId == u).ToListAsync());
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.UserId == u || n.ActorId == u).ToListAsync());
        var profileReports = await db.Reports.Where(r => r.TargetUserId == u && r.Target == ReportTarget.Profile).ToListAsync();
        var profileReportIds = profileReports.Select(r => (int?)r.Id).ToList();
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.Type == NotificationType.ReportOutcome && profileReportIds.Contains(n.SubjectId)).ToListAsync());
        db.Reports.RemoveRange(profileReports);

        var listIds = await db.CustomLists.Where(l => l.UserId == u).Select(l => l.Id).ToListAsync();
        db.CustomListItems.RemoveRange(await db.CustomListItems.Where(i => listIds.Contains(i.ListId)).ToListAsync());
        db.CustomLists.RemoveRange(await db.CustomLists.Where(l => l.UserId == u).ToListAsync());
        db.WatchList.RemoveRange(await db.WatchList.Where(w => w.UserId == u).ToListAsync());
        db.UserBlocks.RemoveRange(await db.UserBlocks.Where(b => b.BlockerId == u || b.BlockedId == u).ToListAsync());
        db.NotificationPreferences.RemoveRange(await db.NotificationPreferences.Where(p => p.UserId == u).ToListAsync());
        db.Sessions.RemoveRange(await db.Sessions.Where(s => s.UserId == u).ToListAsync());
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == u).ToListAsync());
        db.UserLogins.RemoveRange(await db.UserLogins.Where(x => x.UserId == u).ToListAsync());
        db.UserClaims.RemoveRange(await db.UserClaims.Where(x => x.UserId == u).ToListAsync());
        db.UserTokens.RemoveRange(await db.UserTokens.Where(x => x.UserId == u).ToListAsync());

        // the tombstone: a unique throwaway name (Identity needs one), nothing to sign in with, locked for good
        var gone = "deleted-" + u;
        user.UserName = gone; user.NormalizedUserName = gone.ToUpperInvariant();
        user.Email = null; user.NormalizedEmail = null; user.EmailConfirmed = false;
        user.PasswordHash = null; user.SecurityStamp = Guid.NewGuid().ToString();
        user.PhoneNumber = null; user.PhoneNumberConfirmed = false; user.TwoFactorEnabled = false;
        user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.MaxValue; user.AccessFailedCount = 0;
        user.TermsAcceptedAt = null; user.TermsVersion = null;
        user.CreatedAt = new DateTime(user.CreatedAt.Year, user.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc); // only the month joined is kept
        user.DeletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return true;
    }
}
