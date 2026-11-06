using api.Data;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

// Real Postgres, like ReviewPostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
// Stats, recommendations and export queries are translated to SQL here.
public class TrackingPostgresTests
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
    public async Task Recommendations_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await RecommendationTests.AssertRecommendations(() => pg.Db());
    }

    [Fact]
    public async Task Export_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        var (c, _) = await ExportTests.Setup(b => b.UseNpgsql(pg.ConnectionString));
        await ExportTests.AssertJson(c);
        await ExportTests.AssertCsv(c);
    }

    [Fact]
    public async Task Stats_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await StatsTests.AssertStats(() => pg.Db());
    }
}
