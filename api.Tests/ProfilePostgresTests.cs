using api.Data;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ProfileTests;

namespace api.Tests;

// Real Postgres: set TEST_PG, otherwise each test returns at once and proves nothing. Here the migration runs, bytea round-trips
// and the avatar foreign key is enforced.
public class ProfilePostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");

    static async Task Run(Func<Func<AppDbContext>, Task> scenario)
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using (var db = pg.Db()) await db.Database.MigrateAsync();
        await scenario(() => pg.Db());
    }

    [Fact] public Task Uploads_in_sql() => Run(Uploads);
    [Fact] public Task Deletion_clears_in_sql() => Run(DeletionClears);
    [Fact] public Task Admin_clear_in_sql() => Run(AdminClears);
}
