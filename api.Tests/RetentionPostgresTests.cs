using api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static api.Tests.RetentionTests;

namespace api.Tests;

// Real Postgres, like DeletionPostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
public class RetentionPostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");

    [Fact]
    public async Task Retention_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using (var db = pg.Db()) await db.Database.MigrateAsync();
        await RemovesOnlyWhatTheRulesSay(() => pg.Db());
    }

    // a second instance finds the lock taken and does nothing; once the first finishes, the next run goes ahead
    [Fact]
    public async Task A_second_instance_skips_while_the_first_holds_the_lock()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using (var setup = pg.Db()) await setup.Database.MigrateAsync();

        using var first = pg.Db();
        await using (var held = await api.Services.AdvisoryLock.TryTake(first, api.Services.AdvisoryLock.RetentionKey))
        {
            Assert.NotNull(held);
            using var second = pg.Db();
            Assert.Null(await api.Services.RetentionService.RunOnce(second, NullLogger.Instance));
        }
        using var third = pg.Db();
        Assert.NotNull(await api.Services.RetentionService.RunOnce(third, NullLogger.Instance));
    }
}
