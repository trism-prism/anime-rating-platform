using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public record RetentionCounts(int Sessions, int UnverifiedAccounts, int Reports, int Notifications);

// What is kept and for how long (the privacy policy repeats these numbers):
//  - sessions: until they expire or are signed out,
//  - accounts whose email was never verified: 30 days,
//  - reports once settled (dismissed or removed): 1 year,
//  - notifications: 90 days.
// The emailed 6-digit codes are not stored at all (they are derived from the account and expire on their own).
public static class Retention
{
    public const int UnverifiedDays = 30, ReportDays = 365, NotificationDays = 90;
    const int AccountsPerRun = 100; // each goes through the full account deletion; the rest wait for tomorrow

    // a step that fails is logged and skipped, so one bad row cannot stop the others (or tomorrow's run of the same step)
    public static async Task<RetentionCounts> Run(AppDbContext db, DateTime now, ILogger? log = null)
    {
        async Task<int> Step(string name, Func<Task<int>> work)
        {
            try { return await work(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                log?.LogError(ex.GetType().Name + " in retention step {Step}: {Message}", name, ex.Message);
                return 0;
            }
        }

        var sessions = await Step("sessions", () => Purge(db, db.Sessions.Where(s => s.ExpiresAt < now || s.RevokedAt != null)));

        var accounts = await Step("unverified accounts", async () =>
        {
            var cutoff = now.AddDays(-UnverifiedDays);
            // an admin without a verified email is left alone: deleting the last admin would leave nobody to moderate or read support mail
            var stale = await db.Users.Where(u => !u.EmailConfirmed && u.CreatedAt < cutoff && !db.UserRoles.Any(r => r.UserId == u.Id))
                .OrderBy(u => u.CreatedAt).Select(u => u.Id).Take(AccountsPerRun).ToListAsync();
            var n = 0;
            foreach (var id in stale)
            {
                try { if (await AccountDeletion.Delete(db, id)) n++; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log?.LogError(ex.GetType().Name + " deleting unverified account {UserId}: {Message}", id, ex.Message);
                }
                db.ChangeTracker.Clear();
            }
            return n;
        });

        var reportCutoff = now.AddDays(-ReportDays);
        var reports = await Step("reports", () => Purge(db, db.Reports.Where(r => r.Status != ReportStatus.Open && r.ResolvedAt != null && r.ResolvedAt < reportCutoff)));

        var notificationCutoff = now.AddDays(-NotificationDays);
        var notifications = await Step("notifications", () => Purge(db, db.Notifications.Where(n => n.CreatedAt < notificationCutoff)));
        return new RetentionCounts(sessions, accounts, reports, notifications);
    }

    // one DELETE on Postgres; InMemory (the tests) has no ExecuteDelete
    static async Task<int> Purge<T>(AppDbContext db, IQueryable<T> rows) where T : class
    {
        if (db.Database.IsNpgsql()) return await rows.ExecuteDeleteAsync();
        var list = await rows.ToListAsync();
        db.RemoveRange(list);
        await db.SaveChangesAsync();
        return list.Count;
    }
}

// Runs the cleanup about once a day. The Postgres advisory lock keeps two running instances from cleaning at the same moment;
// the cleanup is idempotent, so a second run right after the first finds nothing to do.
public class RetentionService : BackgroundService
{
    readonly IServiceScopeFactory _scopes;
    readonly ILogger<RetentionService> _log;

    public RetentionService(IServiceScopeFactory scopes, ILogger<RetentionService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct); // let the migration and startup finish first
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await RunOnce(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _log);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex.GetType().Name + " in the retention job: {Message}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    // null when another instance holds the lock
    public static async Task<RetentionCounts?> RunOnce(AppDbContext db, ILogger log)
    {
        await using var gate = await AdvisoryLock.TryTake(db, AdvisoryLock.RetentionKey);
        if (gate == null)
        {
            log.LogInformation("Retention job skipped: another instance is running it.");
            return null;
        }
        var c = await Retention.Run(db, DateTime.UtcNow, log);
        log.LogInformation("Retention job removed {Sessions} sessions, {Accounts} unverified accounts, {Reports} settled reports, {Notifications} notifications.",
            c.Sessions, c.UnverifiedAccounts, c.Reports, c.Notifications);
        return c;
    }
}
