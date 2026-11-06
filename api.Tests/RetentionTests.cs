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

    static AppUser Extra(string id, int daysOld, bool confirmed)
    {
        var u = User(id, id);
        u.EmailConfirmed = confirmed;
        u.CreatedAt = Now.AddDays(-daysOld);
        return u;
    }

    internal static async Task RemovesOnlyWhatTheRulesSay(Func<AppDbContext> open)
    {
        await Seed(open); // a b c d, all confirmed and created "now" by the clock, so the cutoffs below are far from them
        using (var db = open())
        {
            db.Users.AddRange(Extra("old-unverified", 40, false), Extra("new-unverified", 5, false), Extra("old-verified", 400, true));
            await db.SaveChangesAsync();
            foreach (var u in await db.Users.ToListAsync()) if (u.Id is "a" or "b" or "c" or "d") u.CreatedAt = Now.AddDays(-1);
            // the never-verified account rated title 1 once: its score must leave the total
            db.Ratings.Add(new Rating { UserId = "old-unverified", TitleId = 1, Story = 8, Art = 8, Characters = 8, Enjoyment = 8, Overall = 8 });
            db.Ratings.Add(new Rating { UserId = "b", TitleId = 1, Story = 6, Art = 6, Characters = 6, Enjoyment = 6, Overall = 6 });
            (await db.Titles.FindAsync(1))!.RatingCount = 2;
            (await db.Titles.FindAsync(1))!.RatingSum = 14;

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

        using (var db = open()) Assert.Equal(new RetentionCounts(2, 1, 2, 1), await Retention.Run(db, Now));

        using (var db = open())
        {
            Assert.Equal(new[] { "a", "b", "c", "d", "new-unverified", "old-verified" }, (await db.Users.Select(u => u.Id).ToListAsync()).Order());
            Assert.Equal(1, await db.Sessions.CountAsync()); // the active one
            Assert.Equal(new[] { ReportStatus.Open, ReportStatus.Dismissed }, (await db.Reports.OrderBy(r => r.Id).Select(r => r.Status).ToListAsync()).OrderBy(s => s));
            Assert.Equal(1, await db.Notifications.CountAsync());
            // the removed account's rating is off the total, bob's stays
            var t = await db.Titles.FindAsync(1);
            Assert.Equal((1, 6.0), (t!.RatingCount, t.RatingSum));
            Assert.Equal(1, await db.Ratings.CountAsync());
        }

        // a second run has nothing left to do
        using (var db = open()) Assert.Equal(new RetentionCounts(0, 0, 0, 0), await Retention.Run(db, Now));
    }

    [Fact]
    public async Task Retention_removes_only_what_the_rules_say() => await RemovesOnlyWhatTheRulesSay(Mem());

    [Fact]
    public async Task A_run_deletes_at_most_one_hundred_unverified_accounts()
    {
        var open = Mem();
        using (var db = open())
        {
            for (var i = 0; i < 105; i++) db.Users.Add(Extra("u" + i, 31, false));
            await db.SaveChangesAsync();
        }
        using (var db = open()) Assert.Equal(100, (await Retention.Run(db, Now)).UnverifiedAccounts);
        using (var db = open()) Assert.Equal(5, await db.Users.CountAsync());
    }

    [Fact]
    public async Task RunOnce_logs_and_returns_the_counts()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.Equal(new RetentionCounts(0, 0, 0, 0), await RetentionService.RunOnce(db, NullLogger.Instance));
    }
}
