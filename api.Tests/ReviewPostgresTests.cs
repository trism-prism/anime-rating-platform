using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// Real Postgres, like SocialPostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
// Here the viewer rule is translated to SQL, the counters really race, and the migration runs on a populated database.
public class ReviewPostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");
    const string BeforeReviews = "20261105181420_AddSocial";

    static async Task<PostgresTests.Throwaway> Migrated()
    {
        var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        return pg;
    }

    [Fact]
    public async Task Viewer_rule_holds_on_every_read_path_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await ViewerRuleOnEveryReadPath(() => pg.Db());
    }

    [Fact]
    public async Task Blocks_hide_content_both_ways_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await BlocksHideContentBothWays(() => pg.Db());
    }

    [Fact]
    public async Task Sort_and_keyset_agree_in_sql()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await SortAndKeysetAgree(() => pg.Db());
    }

    // ---- counters ----

    static async Task AssertCountersMatchRows(PostgresTests.Throwaway pg, int ratingId)
    {
        using var db = pg.Db();
        var r = await db.Ratings.AsNoTracking().SingleAsync(x => x.Id == ratingId);
        var rows = await db.Reactions.Where(x => x.RatingId == ratingId).Select(x => x.Kind).ToListAsync();
        var h = rows.Count(k => k == ReactionKind.Helpful);
        var u = rows.Count(k => k == ReactionKind.Unhelpful);
        var f = rows.Count(k => k == ReactionKind.Funny);
        Assert.Equal((h, u, f, h - u), (r.HelpfulCount, r.UnhelpfulCount, r.FunnyCount, r.Score));
    }

    static async Task AddUsers(PostgresTests.Throwaway pg, int n)
    {
        using var db = pg.Db();
        for (var i = 0; i < n; i++) db.Users.Add(new AppUser { Id = $"u{i:00}", UserName = $"user{i}", NormalizedUserName = $"USER{i}", EmailConfirmed = true });
        await db.SaveChangesAsync();
    }

    static async Task<IActionResult> ReactAs(PostgresTests.Throwaway pg, string me, int id, string? kind)
    {
        using var db = pg.Db();
        var c = As(new ReviewsController(db), me);
        return kind == null ? await c.Unreact(id) : await c.React(id, new ReactionRequest(kind));
    }

    [Fact]
    public async Task Concurrent_reactions_from_many_users_leave_exact_counters()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        await AddUsers(pg, 40);
        var id = await AddRating(() => pg.Db(), "a", 1, "review");
        var kinds = new[] { "Helpful", "Unhelpful", "Funny" };

        // 40 users at once, then each changes or clears at once
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => ReactAs(pg, $"u{i:00}", id, kinds[i % 3])));
        Assert.All(results, r => Assert.True(Status(r) is 200 or 409));
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => ReactAs(pg, $"u{i:00}", id, i % 4 == 0 ? null : kinds[(i + 1) % 3])));
        await AssertCountersMatchRows(pg, id);

        // the same user firing several changes at once: whichever order they land in, the counters match the one row
        for (var round = 0; round < 5; round++)
        {
            await Task.WhenAll(new[] { "Helpful", "Unhelpful", "Funny", null, "Helpful", "Funny" }.Select(k => ReactAs(pg, "b", id, k)));
            await AssertCountersMatchRows(pg, id);
        }
        using var db = pg.Db();
        Assert.True(await db.Reactions.CountAsync(x => x.RatingId == id && x.UserId == "b") <= 1);
        Assert.True(await db.Notifications.CountAsync(n => n.Type == NotificationType.Reaction && n.ActorId == "b") <= 1);
    }

    [Fact]
    public async Task A_change_that_races_another_change_by_the_same_user_is_retried_not_double_counted()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        var id = await AddRating(() => pg.Db(), "a", 1, "review");
        await ReactAs(pg, "b", id, "Helpful");

        // another request turns Bob's Helpful into Funny just before ours turns it into Unhelpful
        var other = $"UPDATE \"Reactions\" SET \"Kind\"='Funny' WHERE \"RatingId\"={id} AND \"UserId\"='b'; " +
                    $"UPDATE \"Ratings\" SET \"HelpfulCount\"=\"HelpfulCount\"-1,\"FunnyCount\"=\"FunnyCount\"+1,\"Score\"=\"Score\"-1 WHERE \"Id\"={id}";
        using (var db = pg.Db(new SocialPostgresTests.Race(pg.ConnectionString, null, "UPDATE \"Reactions\"", other)))
            Assert.Equal(200, Status(await As(new ReviewsController(db), "b").React(id, new ReactionRequest("Unhelpful"))));

        using var check = pg.Db();
        Assert.Equal(ReactionKind.Unhelpful, (await check.Reactions.SingleAsync()).Kind);
        var r = await check.Ratings.AsNoTracking().SingleAsync();
        Assert.Equal((0, 1, 0, -1), (r.HelpfulCount, r.UnhelpfulCount, r.FunnyCount, r.Score));
        await AssertCountersMatchRows(pg, id);
    }

    [Fact]
    public async Task A_clear_that_races_a_change_is_retried_against_the_new_kind()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        var id = await AddRating(() => pg.Db(), "a", 1, "review");
        await ReactAs(pg, "b", id, "Helpful");

        var other = $"UPDATE \"Reactions\" SET \"Kind\"='Funny' WHERE \"RatingId\"={id} AND \"UserId\"='b'; " +
                    $"UPDATE \"Ratings\" SET \"HelpfulCount\"=\"HelpfulCount\"-1,\"FunnyCount\"=\"FunnyCount\"+1,\"Score\"=\"Score\"-1 WHERE \"Id\"={id}";
        using (var db = pg.Db(new SocialPostgresTests.Race(pg.ConnectionString, null, "DELETE FROM \"Reactions\"", other)))
            Assert.Equal(200, Status(await As(new ReviewsController(db), "b").Unreact(id)));

        using var check = pg.Db();
        Assert.Empty(check.Reactions);
        Assert.Equal((0, 0, 0, 0), await Totals(pg, id));
        Assert.Empty(check.Notifications.Where(n => n.Type == NotificationType.Reaction));
    }

    [Fact]
    public async Task A_double_first_click_loses_the_insert_race_and_counts_once()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        var id = await AddRating(() => pg.Db(), "a", 1, "review");

        // the same click arrives twice: the other one's row lands just before ours
        var other = $"INSERT INTO \"Reactions\"(\"RatingId\",\"UserId\",\"Kind\",\"CreatedAt\") VALUES ({id},'b','Helpful',now()); " +
                    $"UPDATE \"Ratings\" SET \"HelpfulCount\"=\"HelpfulCount\"+1,\"Score\"=\"Score\"+1 WHERE \"Id\"={id}";
        using (var db = pg.Db(new SocialPostgresTests.Race(pg.ConnectionString, null, "INSERT INTO \"Reactions\"", other)))
            Assert.Equal(200, Status(await As(new ReviewsController(db), "b").React(id, new ReactionRequest("Helpful"))));

        Assert.Equal((1, 0, 0, 1), await Totals(pg, id));
        await AssertCountersMatchRows(pg, id);
    }

    static async Task<(int, int, int, int)> Totals(PostgresTests.Throwaway pg, int id)
    {
        using var db = pg.Db();
        var r = await db.Ratings.AsNoTracking().SingleAsync(x => x.Id == id);
        return (r.HelpfulCount, r.UnhelpfulCount, r.FunnyCount, r.Score);
    }

    // ---- constraints, cascades ----

    [Fact]
    public async Task Database_allows_one_reaction_per_user_per_review_and_one_reaction_notification_per_reactor()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        var id = await AddRating(() => pg.Db(), "a", 1, "review");
        using var db = pg.Db();
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO \"Reactions\"(\"RatingId\",\"UserId\",\"Kind\",\"CreatedAt\") VALUES ({id},'b','Helpful',now())");
        var dup = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync($"INSERT INTO \"Reactions\"(\"RatingId\",\"UserId\",\"Kind\",\"CreatedAt\") VALUES ({id},'b','Funny',now())"));
        Assert.Equal("23505", dup.SqlState);

        string Note(string type) => $"INSERT INTO \"Notifications\"(\"UserId\",\"Type\",\"ActorId\",\"SubjectId\",\"CreatedAt\") VALUES ('a','{type}','b',{id},now())";
        await db.Database.ExecuteSqlRawAsync(Note("Reaction"));
        var dupNote = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(Note("Reaction")));
        Assert.Equal("23505", dupNote.SqlState);
        await db.Database.ExecuteSqlRawAsync(Note("Reply")); // the index covers Reaction only
        await db.Database.ExecuteSqlRawAsync(Note("Reply"));
    }

    [Fact]
    public async Task Deleting_a_user_or_a_rating_cascades_replies_and_reactions()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        await Seed(() => pg.Db());
        var first = await AddRating(() => pg.Db(), "a", 1, "one");
        var second = await AddRating(() => pg.Db(), "a", 2, "two");
        foreach (var id in new[] { first, second })
        {
            await Call(() => pg.Db(), "b", c => c.PostReply(id, new ReplyRequest("from bob", false)));
            await Call(() => pg.Db(), "c", c => c.PostReply(id, new ReplyRequest("from carol", false)));
            await ReactAs(pg, "b", id, "Helpful");
            await ReactAs(pg, "c", id, "Funny");
        }

        using var db = pg.Db();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'b'");
        Assert.Equal(new[] { "c", "c" }, await db.Replies.Select(r => r.UserId).ToListAsync());
        Assert.Equal(2, await db.Reactions.CountAsync());
        Assert.Empty(db.Notifications.Where(n => n.ActorId == "b")); // the actor's notifications went with them

        await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"Ratings\" WHERE \"Id\" = {first}");
        Assert.Equal(1, await db.Replies.CountAsync());
        Assert.Equal(1, await db.Reactions.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'c'");
        Assert.Equal(0, await db.Replies.CountAsync() + await db.Reactions.CountAsync());
    }

    // ---- the migration on a populated database ----

    [Fact]
    public async Task Migration_makes_everything_that_exists_Only_me_and_new_rows_Public()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReviews);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers"("Id","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES ('u1',true,false,false,false,0),('u2',true,false,false,false,0);
            INSERT INTO "Series"("Name","Kind","Popularity") VALUES ('S','Anime',1);
            INSERT INTO "Titles"("AniListId","Type","Name","Genres","Popularity","SeriesId","Position","Format","SeriesLocked","RatingCount","RatingSum")
              SELECT 1,'Anime','T','',1,"Id",1,'TV',false,0,0 FROM "Series" UNION ALL SELECT 2,'Anime','T2','',1,"Id",2,'TV',false,0,0 FROM "Series";
            INSERT INTO "Ratings"("UserId","TitleId","Story","Art","Characters","Enjoyment","Overall","CreatedAt","ReviewText")
              SELECT 'u1',"Id",8,8,8,8,8,now(),'a written review' FROM "Titles" WHERE "AniListId"=1;
            INSERT INTO "Ratings"("UserId","TitleId","Story","Art","Characters","Enjoyment","Overall","CreatedAt","ReviewText")
              SELECT 'u2',"Id",6,6,6,6,6,now(),'   ' FROM "Titles" WHERE "AniListId"=1;
            INSERT INTO "Ratings"("UserId","TitleId","Story","Art","Characters","Enjoyment","Overall","CreatedAt")
              SELECT 'u1',"Id",5,5,5,5,5,now() FROM "Titles" WHERE "AniListId"=2;
            INSERT INTO "WatchList"("UserId","TitleId","Status","Progress") SELECT 'u1',"Id",'Watching',3 FROM "Titles" WHERE "AniListId"=1;
            """);

        await migrator.MigrateAsync();

        Assert.Equal(new[] { "OnlyMe", "OnlyMe", "OnlyMe" }, await db.Database.SqlQueryRaw<string>("""SELECT "Audience" AS "Value" FROM "Ratings" """).ToListAsync());
        Assert.Equal(new[] { "OnlyMe" }, await db.Database.SqlQueryRaw<string>("""SELECT "Audience" AS "Value" FROM "WatchList" """).ToListAsync());
        // nothing existing is visible to anyone else, and blank text became NULL so it is not a review
        var anon = await Call(() => pg.Db(), null, c => c.ForTitle(1, null, null));
        Assert.Empty(Json(anon).GetProperty("items").EnumerateArray());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "Ratings" WHERE "ReviewText" IS NULL AND "UserId"='u2' """).SingleAsync());
        Assert.Equal(new[] { 0, 0, 0 }, await db.Database.SqlQueryRaw<int>("""SELECT "Score" + "HelpfulCount" + "UnhelpfulCount" + "FunnyCount" AS "Value" FROM "Ratings" """).ToListAsync());

        // new rows default to Public (the migration's backfill default is gone), and a raw insert has to name an audience
        using (var fresh = pg.Db())
        {
            fresh.Ratings.Add(new Rating { UserId = "u2", TitleId = (await fresh.Titles.FirstAsync(t => t.AniListId == 2)).Id, Story = 7, Art = 7, Characters = 7, Enjoyment = 7, Overall = 7 });
            fresh.WatchList.Add(new WatchListEntry { UserId = "u2", TitleId = (await fresh.Titles.FirstAsync(t => t.AniListId == 2)).Id, Status = WatchStatus.Planning });
            await fresh.SaveChangesAsync();
            Assert.Equal(Audience.Public, (await fresh.Ratings.AsNoTracking().SingleAsync(r => r.UserId == "u2" && r.Story == 7)).Audience);
            Assert.Equal(Audience.Public, (await fresh.WatchList.AsNoTracking().SingleAsync(w => w.UserId == "u2")).Audience);
        }
        var missing = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "WatchList"("UserId","TitleId","Status","Progress") SELECT 'u2',"Id",'Planning',0 FROM "Titles" WHERE "AniListId"=1
            """));
        Assert.Equal("23502", missing.SqlState); // not-null violation: no silent default

        // and it goes back down
        await migrator.MigrateAsync(BeforeReviews);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_name IN ('Ratings','WatchList') AND column_name = 'Audience' """).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM information_schema.tables WHERE table_name IN ('Replies','Reactions') """).SingleAsync());
    }

    [Fact]
    public async Task Review_indexes_exist_after_migrating()
    {
        if (Admin == null) return;
        await using var pg = await Migrated();
        using var db = pg.Db();
        var defs = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes
            WHERE indexname IN ('IX_Ratings_TitleId_Score_Id','IX_Ratings_TitleId_Id','IX_Ratings_UserId_Id','IX_Ratings_TitleId','IX_Replies_RatingId_Id','IX_Reactions_RatingId_UserId')
            """).ToListAsync();
        Assert.Equal(6, defs.Count);
        Assert.Equal(2, defs.Count(d => d.Contains("WHERE (\"ReviewText\" IS NOT NULL)")));
        Assert.Contains(defs, d => d.Contains("UNIQUE") && d.Contains("RatingId") && d.Contains("UserId"));
    }
}
