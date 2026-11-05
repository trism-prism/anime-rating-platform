using System.Data.Common;
using System.Net;
using System.Text;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace api.Tests;

// These need a real Postgres: set TEST_PG to a connection string for the server (any database, e.g. Database=postgres).
// Each test makes a throwaway database and drops it. Without TEST_PG they return at once, so a plain `dotnet test`
// does not prove them; the migration, the lock and the guarded write cannot be checked on InMemory.
public class PostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");
    const string BeforeSeries = "20261105141812_RemoveTwoFactor", SeriesMigration = "20261105145714_AddSeries";

    class Throwaway : IAsyncDisposable
    {
        readonly string _name = "series_test_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }

        Throwaway() => ConnectionString = new NpgsqlConnectionStringBuilder(Admin!) { Database = _name }.ToString();

        public static async Task<Throwaway> Create()
        {
            var t = new Throwaway();
            await using var c = new NpgsqlConnection(Admin);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{t._name}\"", c);
            await cmd.ExecuteNonQueryAsync();
            return t;
        }

        public AppDbContext Db(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(Admin);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migration_gives_existing_titles_a_series_and_goes_back_down_with_volume_ratings()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using var db = pg.Db();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSeries);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers"("Id","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES ('u1',true,false,false,false,0),('u2',true,false,false,false,0);
            INSERT INTO "Titles"("AniListId","Type","Name","CoverUrl","Genres","Popularity") VALUES (10,'Anime','Show A','http://c/a','',50),(11,'Manga','Manga B',NULL,'',7);
            INSERT INTO "Ratings"("UserId","TitleId","Story","Art","Characters","Enjoyment","Overall","CreatedAt") SELECT 'u1',"Id",8,8,8,8,8,now() FROM "Titles" WHERE "AniListId"=10;
            INSERT INTO "Ratings"("UserId","TitleId","Story","Art","Characters","Enjoyment","Overall","CreatedAt") SELECT 'u2',"Id",6,6,6,6,6.5,now() FROM "Titles" WHERE "AniListId"=10;
            """);

        await migrator.MigrateAsync(SeriesMigration);

        var titles = await db.Titles.Include(t => t.Series).OrderBy(t => t.AniListId).ToListAsync();
        Assert.Equal(new[] { "Show A", "Manga B" }, titles.Select(t => t.Series.Name));
        Assert.Equal(new[] { TitleType.Anime, TitleType.Manga }, titles.Select(t => t.Series.Kind));
        Assert.Equal(new[] { 50, 7 }, titles.Select(t => t.Series.Popularity));
        Assert.Equal(new[] { 2, 0 }, titles.Select(t => t.RatingCount));
        Assert.Equal(14.5, titles[0].RatingSum);

        // a new series gets a fresh id, not one a backfilled series already has
        db.Series.Add(new Series { Name = "New" });
        await db.SaveChangesAsync();

        // a volume rating exists: Down must not trip over the recreated unique (UserId, TitleId) index
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Ratings"("UserId","TitleId","Volume","Story","Art","Characters","Enjoyment","Overall","CreatedAt") SELECT 'u1',"Id",1,5,5,5,5,5,now() FROM "Titles" WHERE "AniListId"=11;
            """);
        await migrator.MigrateAsync(BeforeSeries);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "Ratings" """).SingleAsync());
    }

    [Fact]
    public async Task Trigram_indexes_exist_after_migrating()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();

        var defs = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname IN ('IX_Series_Name','IX_Titles_Name')
            """).ToListAsync();
        Assert.Equal(2, defs.Count);
        Assert.All(defs, d => Assert.Contains("gin_trgm_ops", d));
    }

    // runs a statement on another connection (an admin acting concurrently) right before the first command containing `when`
    class OtherSessionFirst : DbCommandInterceptor
    {
        readonly string _cs, _when, _sql;
        bool _done;
        public OtherSessionFirst(string cs, string when, string sql) { _cs = cs; _when = when; _sql = sql; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!_done && command.CommandText.Contains(_when))
            {
                _done = true;
                await using var c = new NpgsqlConnection(_cs);
                await c.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand(_sql, c);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return result;
        }
    }

    [Fact]
    public async Task Assigner_does_not_move_a_title_an_admin_locked_during_the_run()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using (var seed = pg.Db())
        {
            await seed.Database.MigrateAsync();
            seed.Titles.AddRange(
                new Title { AniListId = 1, Name = "Show", Year = 2020, Series = new Series { Name = "Show" } },
                new Title { AniListId = 2, Name = "Show 2", Year = 2021, Series = new Series { Name = "Show 2" } });
            seed.TitleRelations.Add(new TitleRelation { FromAniListId = 1, ToAniListId = 2, Type = "SEQUEL" });
            await seed.SaveChangesAsync();
        }
        int before;
        using (var read = pg.Db()) before = (await read.Titles.SingleAsync(t => t.AniListId == 2)).SeriesId;

        using (var db = pg.Db(new OtherSessionFirst(pg.ConnectionString, "UPDATE \"Titles\"", "UPDATE \"Titles\" SET \"SeriesLocked\" = true WHERE \"AniListId\" = 2")))
            await new SeriesAssigner(db).Assign();

        using var check = pg.Db();
        var two = await check.Titles.SingleAsync(t => t.AniListId == 2);
        Assert.True(two.SeriesLocked);
        Assert.Equal(before, two.SeriesId);
    }

    [Fact]
    public async Task Refresh_does_not_write_a_position_onto_a_title_moved_out_meanwhile()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using (var seed = pg.Db())
        {
            await seed.Database.MigrateAsync();
            var other = new Series { Name = "Other" };
            seed.Titles.AddRange(
                new Title { AniListId = 1, Name = "Show", Year = 2020, Series = new Series { Name = "Show" } },
                new Title { AniListId = 2, Name = "Show 2", Year = 2021, Series = new Series { Name = "Show" } });
            seed.Series.Add(other);
            await seed.SaveChangesAsync();
            // both titles share the first series
            var first = (await seed.Titles.SingleAsync(t => t.AniListId == 1)).SeriesId;
            await seed.Database.ExecuteSqlRawAsync("UPDATE \"Titles\" SET \"SeriesId\" = {0}, \"Position\" = 0", first);
        }
        int otherId;
        using (var read = pg.Db()) otherId = (await read.Series.SingleAsync(s => s.Name == "Other")).Id;

        // when Refresh writes the first position, an admin moves title 2 to the other series
        using (var db = pg.Db(new OtherSessionFirst(pg.ConnectionString, "SET \"Position\"",
            $"UPDATE \"Titles\" SET \"SeriesId\" = {otherId} WHERE \"AniListId\" = 2")))
            await new SeriesAssigner(db).Refresh();

        using var check = pg.Db();
        var two = await check.Titles.SingleAsync(t => t.AniListId == 2);
        Assert.Equal(otherId, two.SeriesId);
        Assert.Equal(0, two.Position); // left alone
        Assert.Equal(1, (await check.Titles.SingleAsync(t => t.AniListId == 1)).Position);
    }

    [Fact]
    public async Task Deleting_a_series_that_still_has_titles_is_refused()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        db.Titles.Add(new Title { AniListId = 1, Name = "Show", Series = new Series { Name = "Show" } });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"Series\""));
        Assert.Equal(1, await db.Titles.CountAsync());
    }

    [Fact]
    public async Task Admin_move_waits_while_the_series_lock_is_held()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        db.Titles.Add(new Title { AniListId = 1, Name = "Show", Series = new Series { Name = "Show" } });
        await db.SaveChangesAsync();
        var id = (await db.Titles.SingleAsync()).Id;

        Task<Microsoft.AspNetCore.Mvc.IActionResult> move;
        await using (var other = new NpgsqlConnection(pg.ConnectionString))
        {
            await other.OpenAsync();
            await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({AdvisoryLock.SeriesKey})", other))
                await take.ExecuteNonQueryAsync();

            move = new api.Controllers.AdminSeriesController(db).Move(id, new api.Controllers.MoveRequest(null));
            await Task.Delay(700);
            Assert.False(move.IsCompleted); // Refresh and admin edits take turns

            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLock.SeriesKey})", other);
            await release.ExecuteNonQueryAsync();
        }
        await move.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True((await db.Titles.AsNoTracking().SingleAsync()).SeriesLocked);
    }

    sealed class EmptyAniList : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""data"":{""Page"":{""media"":[]}}}", Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task Import_refuses_to_run_while_another_import_holds_the_lock_and_releases_it_after()
    {
        if (Admin == null) return;
        await using var pg = await Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        var client = new AniListClient(new HttpClient(new EmptyAniList())) { DelayMs = 0 };

        await using (var other = new NpgsqlConnection(pg.ConnectionString))
        {
            await other.OpenAsync();
            await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({AdvisoryLock.ImportKey})", other))
                await take.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AniListImporter(db, client).Import(1));
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLock.ImportKey})", other);
            await release.ExecuteNonQueryAsync(); // a pooled connection would keep the lock after Close
        }

        await new AniListImporter(db, client).Import(1);
        await new AniListImporter(db, client).Import(1); // the first run unlocked
    }
}
