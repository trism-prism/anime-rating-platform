using api.Data;
using api.Models;
using api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// The daily cleanup. The scenario runs on InMemory here and on Postgres in RetentionPostgresTests (ExecuteDelete and the lock).
public class RetentionTests
{
    static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc); // Utc: Npgsql rejects an unspecified kind

    internal static async Task RemovesOnlyWhatTheRulesSay(Func<AppDbContext> open)
    {
        await Seed(open);
        using (var db = open())
        {
            // an old account that never verified its email is no longer removed
            var old = User("old", "old");
            old.CreatedAt = Now.AddDays(-400);
            db.Users.Add(old);

            Session S(string user, DateTime expires, DateTime? revoked) =>
                new() { Id = Guid.NewGuid(), UserId = user, ExpiresAt = expires, RevokedAt = revoked, CurrentTokenHash = Guid.NewGuid().ToString() };
            db.Sessions.AddRange(S("b", Now.AddDays(-1), null), S("b", Now.AddDays(10), Now.AddDays(-1)), S("b", Now.AddDays(10), null));

            Report R(ReportStatus s, int ageDays) =>
                new() { ReporterId = "b", Target = ReportTarget.Profile, TargetUserId = "c", Reason = ReportReason.Spam, Snapshot = "x", Status = s,
                        CreatedAt = Now.AddDays(-ageDays), ResolvedAt = s == ReportStatus.Open ? null : Now.AddDays(-ageDays) };
            db.Reports.AddRange(R(ReportStatus.Dismissed, 400), R(ReportStatus.Removed, 366), R(ReportStatus.Dismissed, 10), R(ReportStatus.Open, 400));

            db.Notifications.AddRange(
                new Notification { UserId = "b", ActorId = "c", Type = NotificationType.FriendAccepted, CreatedAt = Now.AddDays(-100) },
                new Notification { UserId = "b", ActorId = "c", Type = NotificationType.FriendAccepted, CreatedAt = Now.AddDays(-10) });
            await db.SaveChangesAsync();
        }

        using (var db = open()) Assert.Equal(new RetentionCounts(2, 2, 1), await Retention.Run(db, Now));

        using (var db = open())
        {
            Assert.Equal(5, await db.Users.CountAsync());
            Assert.Equal(1, await db.Sessions.CountAsync()); // the active one
            Assert.Equal(new[] { ReportStatus.Open, ReportStatus.Dismissed }, (await db.Reports.OrderBy(r => r.Id).Select(r => r.Status).ToListAsync()).OrderBy(s => s));
            Assert.Equal(1, await db.Notifications.CountAsync());
        }

        // a second run has nothing left to do
        using (var db = open()) Assert.Equal(new RetentionCounts(0, 0, 0), await Retention.Run(db, Now));
    }

    [Fact]
    public async Task Retention_removes_only_what_the_rules_say() => await RemovesOnlyWhatTheRulesSay(Mem());

    [Fact]
    public async Task RunOnce_logs_and_returns_the_counts()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.Equal(new RetentionCounts(0, 0, 0), await RetentionService.RunOnce(db, NullLogger.Instance));
    }
}
