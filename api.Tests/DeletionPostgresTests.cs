using Microsoft.EntityFrameworkCore;
using static api.Tests.DeletionTests;

namespace api.Tests;

// Real Postgres, like ReportPostgresTests: set TEST_PG, otherwise the test returns at once and proves nothing.
// Here the migration runs and the foreign keys are enforced.
public class DeletionPostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");

    [Fact]
    public async Task Deletion_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using (var db = pg.Db()) await db.Database.MigrateAsync();
        await AnonymizesAndKeepsTheContent(() => pg.Db());
    }
}
