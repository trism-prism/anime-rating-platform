using System.Data.Common;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static api.Tests.SocialTests;

namespace api.Tests;

// Real Postgres, like PostgresTests: set TEST_PG, otherwise each test returns at once and proves nothing.
// The unique pair index, the check constraints, the cascades and the query translation can't be checked on InMemory.
public class SocialPostgresTests
{
    static readonly string? Admin = Environment.GetEnvironmentVariable("TEST_PG");
    const string BeforeSocial = "20261105151304_AddTrigramSearch";

    static async Task<string> SqlState(AppDbContext db, string sql)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        return ex.SqlState!;
    }

    const string Unique = "23505", Check = "23514";

    // Runs `sql` on another connection (another request, as it were) right before the first command containing `when`,
    // counting only commands after one containing `after` has run (null: from the start). This makes a race deterministic.
    class Race : DbCommandInterceptor
    {
        readonly string _cs, _when, _sql;
        string? _after;
        bool _done;
        public Race(string cs, string? after, string when, string sql) { _cs = cs; _after = after; _when = when; _sql = sql; }

        async Task Maybe(DbCommand command, CancellationToken ct)
        {
            if (_done) return;
            var text = command.CommandText;
            if (_after != null)
            {
                if (text.Contains(_after)) _after = null;
                return;
            }
            if (!text.Contains(_when)) return;
            _done = true;
            await using var c = new NpgsqlConnection(_cs);
            await c.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(_sql, c);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            await Maybe(command, ct);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            await Maybe(command, ct);
            return result;
        }
    }

    // a migrated throwaway database with users a, b, c; `pending` also has a request a -> b (id 1) and Bob's notification for it
    static async Task<PostgresTests.Throwaway> Seeded(bool pending = false, bool accepted = false)
    {
        var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        await Db(db);
        if (pending || accepted)
        {
            db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "b", RequesterId = "a", Status = accepted ? FriendStatus.Accepted : FriendStatus.Pending });
            await db.SaveChangesAsync();
            if (pending) db.Notifications.Add(new Notification { UserId = "b", Type = NotificationType.FriendRequest, ActorId = "a", SubjectId = 1 });
            await db.SaveChangesAsync();
        }
        return pg;
    }

    const string InsertFriendship = "INSERT INTO \"Friendships\""; // EF writes a space before the column list, so match on the table only
    const string InsertOtherAsker = "INSERT INTO \"Friendships\"(\"UserLowId\",\"UserHighId\",\"RequesterId\",\"Status\",\"CreatedAt\") VALUES ('a','b','b','Pending',now())";
    const string DeleteFriendship = "DELETE FROM \"Friendships\"";
    const string AcceptedBySomeoneElse = "UPDATE \"Friendships\" SET \"Status\" = 'Accepted'";

    [Fact]
    public async Task Block_ends_with_the_block_even_when_the_friendship_is_deleted_under_it()
    {
        if (Admin == null) return;
        await using var pg = await Seeded(accepted: true);
        using (var db = pg.Db(new Race(pg.ConnectionString, null, DeleteFriendship, DeleteFriendship)))
            Assert.Equal(204, Status(await Friends(db, "a").Block("bob")));

        using var check = pg.Db();
        Assert.Equal(1, await check.UserBlocks.CountAsync(b => b.BlockerId == "a" && b.BlockedId == "b"));
        Assert.Equal(0, await check.Friendships.CountAsync());
    }

    [Fact]
    public async Task A_decline_loaded_before_an_accept_cannot_delete_the_friendship()
    {
        if (Admin == null) return;
        await using var pg = await Seeded(pending: true);
        using (var db = pg.Db(new Race(pg.ConnectionString, null, DeleteFriendship, AcceptedBySomeoneElse)))
            Assert.Equal(404, Status(await Friends(db, "b").Decline(1)));

        using var check = pg.Db();
        Assert.Equal(FriendStatus.Accepted, (await check.Friendships.SingleAsync()).Status);
        Assert.Equal(1, await check.Notifications.CountAsync()); // its notification was not deleted with it (the whole save rolled back)
    }

    [Fact]
    public async Task A_cancel_loaded_before_an_accept_cannot_delete_the_friendship()
    {
        if (Admin == null) return;
        await using var pg = await Seeded(pending: true);
        using (var db = pg.Db(new Race(pg.ConnectionString, null, DeleteFriendship, AcceptedBySomeoneElse)))
            Assert.Equal(404, Status(await Friends(db, "a").Cancel(1)));

        using var check = pg.Db();
        Assert.Equal(FriendStatus.Accepted, (await check.Friendships.SingleAsync()).Status);
    }

    [Fact]
    public async Task Accept_after_a_concurrent_decline_is_a_404_and_tells_nobody()
    {
        if (Admin == null) return;
        await using var pg = await Seeded(pending: true);
        using (var db = pg.Db(new Race(pg.ConnectionString, null, "UPDATE \"Friendships\"", DeleteFriendship)))
            Assert.Equal(404, Status(await Friends(db, "b").Accept(1)));

        using var check = pg.Db();
        Assert.Empty(await check.Friendships.ToListAsync());
        Assert.Empty(await check.Notifications.Where(n => n.Type == NotificationType.FriendAccepted).ToListAsync());
    }

    [Fact]
    public async Task A_second_accept_does_not_notify_again()
    {
        if (Admin == null) return;
        // the other session wins between this request's read and its write, and sends the one notification
        await using var pg = await Seeded(pending: true);
        var winner = AcceptedBySomeoneElse + "; INSERT INTO \"Notifications\"(\"UserId\",\"Type\",\"ActorId\",\"SubjectId\",\"CreatedAt\") VALUES ('a','FriendAccepted','b',1,now())";
        using (var db = pg.Db(new Race(pg.ConnectionString, null, "UPDATE \"Friendships\"", winner)))
            Assert.Equal(404, Status(await Friends(db, "b").Accept(1)));
        using (var check = pg.Db())
            Assert.Equal(1, await check.Notifications.CountAsync(n => n.Type == NotificationType.FriendAccepted));

        // and two really simultaneous accepts: exactly one wins
        await using var pg2 = await Seeded(pending: true);
        using var one = pg2.Db();
        using var two = pg2.Db();
        var codes = await Task.WhenAll(Friends(one, "b").Accept(1), Friends(two, "b").Accept(1));
        Assert.Equal(new[] { 204, 404 }, codes.Select(Status).OrderBy(x => x));
        using var check2 = pg2.Db();
        Assert.Equal(1, await check2.Notifications.CountAsync(n => n.Type == NotificationType.FriendAccepted));
    }

    [Theory]
    [InlineData("b", "a", 404)] // the target blocked me: same answer as an unknown name
    [InlineData("a", "b", 409)] // I blocked them from another tab
    public async Task A_block_that_lands_during_a_send_removes_the_new_request(string blocker, string blocked, int expected)
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        var block = $"INSERT INTO \"UserBlocks\"(\"BlockerId\",\"BlockedId\",\"CreatedAt\") VALUES ('{blocker}','{blocked}',now())";
        using (var db = pg.Db(new Race(pg.ConnectionString, InsertFriendship, "\"UserBlocks\"", block)))
        {
            var r = await Friends(db, "a").Send(new SendFriendRequest("bob"));
            Assert.Equal(expected, Status(r));
            if (expected == 404) Assert.Equal(Reply(await Friends(db, "a").Send(new SendFriendRequest("nobody"))), Reply(r));
        }

        using var check = pg.Db();
        Assert.Empty(await check.Friendships.ToListAsync());
        Assert.Empty(await check.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Requests_sent_at_the_same_moment_end_up_as_friends()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using (var db = pg.Db(new Race(pg.ConnectionString, null, InsertFriendship, InsertOtherAsker)))
        {
            var r = await Friends(db, "a").Send(new SendFriendRequest("bob"));
            Assert.Equal(200, Status(r));
            Assert.Equal("Friends", Json(r).GetProperty("state").GetString());
        }

        using var check = pg.Db();
        Assert.Equal(FriendStatus.Accepted, (await check.Friendships.SingleAsync()).Status);
        Assert.Equal(1, await check.Notifications.CountAsync(n => n.UserId == "b" && n.Type == NotificationType.FriendAccepted)); // the other asker hears of it
    }

    [Fact]
    public async Task A_failed_insert_that_is_not_a_duplicate_does_not_claim_one_exists()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using (var db = pg.Db(new Race(pg.ConnectionString, null, InsertFriendship, "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'b'")))
            Assert.Equal(404, Status(await Friends(db, "a").Send(new SendFriendRequest("bob")))); // foreign key: Bob vanished, so not a 409

        using var check = pg.Db();
        Assert.Empty(await check.Friendships.ToListAsync());
    }

    [Fact]
    public async Task A_request_is_never_saved_without_its_notification()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using (var db = pg.Db(new Race(pg.ConnectionString, null, "INSERT INTO \"Notifications\"", "ALTER TABLE \"Notifications\" ADD CONSTRAINT \"fail_it\" CHECK (false)")))
            Assert.NotEqual(200, Status(await Friends(db, "a").Send(new SendFriendRequest("bob"))));

        using var check = pg.Db();
        Assert.Empty(await check.Friendships.ToListAsync()); // the pending row went back with the failed notification
    }

    // n users u0.. who each asked carol, `days` days ago, with her notification; ids sort after "c", so carol is the low side
#pragma warning disable EF1002 // n and days are ints from the test itself
    static async Task SqlFlood(AppDbContext db, int n, int days)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "AspNetUsers"("Id","UserName","NormalizedUserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","CreatedAt")
              SELECT 'u'||g, 'u'||g, 'U'||g, true,false,false,false,0,now() FROM generate_series(1,{n}) g;
            INSERT INTO "Friendships"("UserLowId","UserHighId","RequesterId","Status","CreatedAt")
              SELECT 'c','u'||g,'u'||g,'Pending', now() - interval '{days} days' FROM generate_series(1,{n}) g;
            INSERT INTO "Notifications"("UserId","Type","ActorId","SubjectId","CreatedAt")
              SELECT 'c','FriendRequest',"RequesterId","Id", now() FROM "Friendships" WHERE "UserLowId" = 'c';
            """);
    }

#pragma warning restore EF1002
    [Fact]
    public async Task Expired_requests_are_pruned_on_postgres_so_a_full_inbox_still_takes_a_new_one()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using var db = pg.Db();
        await SqlFlood(db, FriendsController.MaxPendingIncoming, days: 31);

        Assert.Equal(200, Status(await Friends(db, "a").Send(new SendFriendRequest("carol"))));

        Assert.Equal(1, await db.Friendships.CountAsync());
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task A_full_inbox_of_fresh_requests_refuses_and_decline_all_empties_it_on_postgres()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using var db = pg.Db();
        await SqlFlood(db, FriendsController.MaxPendingIncoming, days: 1);

        Assert.Equal(409, Status(await Friends(db, "a").Send(new SendFriendRequest("carol"))));
        Assert.Equal(204, Status(await Friends(db, "c").DeclineAll()));

        Assert.Equal(0, await db.Friendships.CountAsync());
        Assert.Equal(0, await db.Notifications.CountAsync());
        Assert.Equal(200, Status(await Friends(db, "a").Send(new SendFriendRequest("carol"))));
    }

    [Fact]
    public async Task Decline_all_keeps_a_request_that_was_accepted_at_that_moment()
    {
        if (Admin == null) return;
        await using var pg = await Seeded();
        using (var seed = pg.Db()) await SqlFlood(seed, 5, days: 1);
        var friendshipId = await pg.Db().Friendships.OrderBy(f => f.Id).Select(f => f.Id).FirstAsync();

        using (var db = pg.Db(new Race(pg.ConnectionString, null, DeleteFriendship, AcceptedBySomeoneElse + $" WHERE \"Id\" = {friendshipId}")))
            Assert.Equal(204, Status(await Friends(db, "c").DeclineAll()));

        using var check = pg.Db();
        var left = await check.Friendships.SingleAsync();
        Assert.Equal((friendshipId, FriendStatus.Accepted), (left.Id, left.Status)); // the accepted one survived, the other four went
    }

    [Fact]
    public async Task Notification_indexes_exist_after_migrating()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        var names = await db.Database.SqlQueryRaw<string>("""SELECT indexname AS "Value" FROM pg_indexes WHERE tablename = 'Notifications' """).ToListAsync();
        Assert.Contains("IX_Notifications_SubjectId", names);
        Assert.Contains("IX_Notifications_UserId_Id", names);
    }

    [Fact]
    public async Task Pair_index_and_check_constraints_hold()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        await Db(db);

        const string cols = """INSERT INTO "Friendships"("UserLowId","UserHighId","RequesterId","Status","CreatedAt")""";
        await db.Database.ExecuteSqlRawAsync(cols + """ VALUES ('a','b','a','Pending',now()) """);
        Assert.Equal(Unique, await SqlState(db, cols + """ VALUES ('a','b','b','Pending',now()) """)); // the same pair twice
        Assert.Equal(Check, await SqlState(db, cols + """ VALUES ('c','a','c','Pending',now()) """)); // not in canonical order
        Assert.Equal(Check, await SqlState(db, cols + """ VALUES ('a','c','b','Pending',now()) """)); // requester is not one of the pair
        Assert.Equal(Check, await SqlState(db, cols + """ VALUES ('a','a','a','Pending',now()) """));

        // the order is byte-wise whatever the database collation: 'B' < 'a', as Friendship.Order says
        Assert.Equal(("B", "a"), Friendship.Order("a", "B"));
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "AspNetUsers"("Id","UserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","CreatedAt") VALUES ('B','b2',true,false,false,false,0,now()),('a2','a2',true,false,false,false,0,now()) """);
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "AspNetUsers"("Id","UserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","CreatedAt") VALUES ('a','a3',true,false,false,false,0,now()) ON CONFLICT DO NOTHING """);
        await db.Database.ExecuteSqlRawAsync(cols + """ VALUES ('B','a2','B','Pending',now()) """);

        const string block = """INSERT INTO "UserBlocks"("BlockerId","BlockedId","CreatedAt")""";
        Assert.Equal(Check, await SqlState(db, block + """ VALUES ('a','a',now()) """));
        await db.Database.ExecuteSqlRawAsync(block + """ VALUES ('a','b',now()) """);
        Assert.Equal(Unique, await SqlState(db, block + """ VALUES ('a','b',now()) """));
        await db.Database.ExecuteSqlRawAsync(block + """ VALUES ('b','a',now()) """); // blocking back is a different row
    }

    [Fact]
    public async Task Deleting_a_user_removes_every_social_row()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        await Db(db);

        db.Friendships.Add(new Friendship { UserLowId = "a", UserHighId = "b", RequesterId = "b", Status = FriendStatus.Accepted });
        db.UserBlocks.Add(new UserBlock { BlockerId = "c", BlockedId = "a" });
        db.Notifications.AddRange(
            new Notification { UserId = "a", Type = NotificationType.FriendRequest, ActorId = "b" },
            new Notification { UserId = "b", Type = NotificationType.FriendAccepted, ActorId = "a" }); // a is only the actor here
        db.NotificationPreferences.Add(new NotificationPreference { UserId = "a", Type = NotificationType.FriendRequest, Enabled = false });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "AspNetUsers" WHERE "Id" = 'a' """);

        Assert.Empty(await db.Friendships.ToListAsync());
        Assert.Empty(await db.UserBlocks.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.NotificationPreferences.ToListAsync());
        Assert.Equal(2, await db.Users.CountAsync()); // only the account went
    }

    [Fact]
    public async Task Migration_dates_existing_accounts_and_goes_back_down()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeSocial);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers"("Id","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES ('old',true,false,false,false,0)
            """);

        await migrator.MigrateAsync();

        var joined = await db.Users.Where(u => u.Id == "old").Select(u => u.CreatedAt).SingleAsync();
        Assert.True(joined > DateTime.UtcNow.AddMinutes(-5)); // the migration date, not 0001-01-01
        await migrator.MigrateAsync(BeforeSocial);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM pg_tables WHERE tablename IN ('Friendships','UserBlocks','Notifications','NotificationPreferences') """).SingleAsync());
    }

    [Fact]
    public async Task The_whole_flow_runs_on_postgres_and_the_queries_translate()
    {
        if (Admin == null) return;
        await using var pg = await PostgresTests.Throwaway.Create();
        using var db = pg.Db();
        await db.Database.MigrateAsync();
        await Db(db);

        Assert.Equal(200, Status(await Friends(db, "a").Send(new SendFriendRequest("bob"))));
        Assert.Equal(409, Status(await Friends(db, "a").Send(new SendFriendRequest("bob")))); // the pair index is behind this too
        var bell = Json(await Bell(db, "b").List(null)).GetProperty("items");
        Assert.True(bell[0].GetProperty("actionable").GetBoolean()); // the pending-state subquery
        var id = bell[0].GetProperty("requestId").GetInt32();
        Assert.Equal(1, Json(await Bell(db, "b").UnreadCount()).GetProperty("count").GetInt32());

        var list = Json(await Friends(db, "b").List());
        Assert.Equal(1, list.GetProperty("incoming").GetArrayLength());
        Assert.Equal("Pending", "Pending"); // the stored value is the enum name
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "Friendships" WHERE "Status" = 'Pending' """).SingleAsync());

        Assert.Equal(204, Status(await Friends(db, "b").Accept(id)));
        Assert.Equal("Friends", Json(await Profiles(db, "a").Get("Bob")).GetProperty("viewerState").GetString());
        Assert.Equal(1, Json(await Profiles(db, null).Get("alice")).GetProperty("friendCount").GetInt32());
        Assert.False(Json(await Bell(db, "b").List(null)).GetProperty("items")[0].GetProperty("actionable").GetBoolean());
        Assert.Equal(204, Status(await Bell(db, "a").MarkAllRead()));
        Assert.Equal(0, Json(await Bell(db, "a").UnreadCount()).GetProperty("count").GetInt32());

        // paging past one page
        for (var i = 0; i < 25; i++) db.Notifications.Add(new Notification { UserId = "c", Type = NotificationType.FriendAccepted, ActorId = "a" });
        await db.SaveChangesAsync();
        var page1 = Json(await Bell(db, "c").List(null));
        var page2 = Json(await Bell(db, "c").List(page1.GetProperty("nextCursor").GetInt32()));
        Assert.Equal((20, 5), (page1.GetProperty("items").GetArrayLength(), page2.GetProperty("items").GetArrayLength()));

        Assert.Equal(204, Status(await Friends(db, "a").Block("bob")));
        Assert.Equal(404, Status(await Profiles(db, "b").Get("alice")));
        Assert.Equal(0, await db.Friendships.CountAsync());
        Assert.Equal(200, Status(await Bell(db, "c").SetPreference("FriendRequest", new PreferenceRequest(false))));
        Assert.Equal(200, Status(await Bell(db, "c").SetPreference("FriendRequest", new PreferenceRequest(true)))); // update, not a second insert
        Assert.Equal(1, await db.NotificationPreferences.CountAsync());
    }
}
