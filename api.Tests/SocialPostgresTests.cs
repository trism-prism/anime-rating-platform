using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
