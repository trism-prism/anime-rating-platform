using Microsoft.EntityFrameworkCore;
using static api.Tests.ReportTests;

namespace api.Tests;

// Real Postgres, like ReviewPostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
// Here the migration runs, the open-report unique index is enforced and the feed query is translated to SQL.
public class ReportPostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");

    static async Task<PostgresTests.Throwaway> Migrated()
    {
        var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        return pg;
    }

    [Fact]
    public async Task Filing_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await FilingFollowsTheViewerRuleAndTheLimits(() => pg.Db());
    }

    [Fact]
    public async Task Queue_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await QueueResolvesAndTellsTheReporters(() => pg.Db());
    }

    [Fact]
    public async Task Queue_pages_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await QueuePagesByKeyset(() => pg.Db());
    }

    [Fact]
    public async Task Feed_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await FeedShowsFriendsOnlyAndKeepsAudiencesAndBlocks(() => pg.Db());
    }

    [Fact]
    public async Task Feed_pages_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await FeedPagesByKeyset(() => pg.Db());
    }

    // two open reports by one reporter on one target cannot exist, whatever the app does
    [Fact]
    public async Task Database_refuses_a_second_open_report_but_allows_one_after_resolving()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await ReviewTests.Seed(() => pg.Db());
        Models.Report Make(Models.ReportStatus s) => new() { ReporterId = "c", Target = Models.ReportTarget.Review, TargetId = 1, TargetUserId = "a", Reason = Models.ReportReason.Spam, Status = s };
        using (var db = pg.Db()) { db.Reports.Add(Make(Models.ReportStatus.Dismissed)); db.Reports.Add(Make(Models.ReportStatus.Open)); await db.SaveChangesAsync(); }
        using (var db = pg.Db())
        {
            db.Reports.Add(Make(Models.ReportStatus.Open));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
