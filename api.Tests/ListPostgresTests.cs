using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace api.Tests;

// Real Postgres, like ReviewPostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
public class ListPostgresTests
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
    public async Task Viewer_rule_for_lists_holds_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await ListTests.ViewerRuleOnEveryReadPath(() => pg.Db());
    }

    [Fact]
    public async Task Blocks_hide_lists_both_ways_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await ListTests.BlocksHideListsBothWays(() => pg.Db());
    }

    [Fact]
    public async Task Database_refuses_a_double_item_a_bad_target_and_cascades()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await ReviewTests.Seed(() => pg.Db());
        using var db = pg.Db();
        db.CustomLists.Add(new CustomList { Id = 1, UserId = "a", Name = "L" });
        await db.SaveChangesAsync();

        async Task<string> Fails(string sql) => (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState!;
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "CustomListItems"("ListId","TitleId","AddedAt") VALUES (1,1,now())""");
        Assert.Equal("23505", await Fails("""INSERT INTO "CustomListItems"("ListId","TitleId","AddedAt") VALUES (1,1,now())"""));
        Assert.Equal("23514", await Fails("""INSERT INTO "CustomListItems"("ListId","TitleId","SeriesId","AddedAt") VALUES (1,2,1,now())"""));
        Assert.Equal("23514", await Fails("""INSERT INTO "CustomListItems"("ListId","AddedAt") VALUES (1,now())"""));

        // deleting the user takes their lists and items
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "AspNetUsers" WHERE "Id" = 'a'""");
        Assert.Equal(0, await db.CustomListItems.CountAsync());
    }
}
