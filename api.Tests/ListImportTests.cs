using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static api.Tests.SocialTests;

namespace api.Tests;

public class ListImportTests
{
    class Fake : HttpMessageHandler
    {
        public int Calls;
        public bool AlwaysMore;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Task? Gate; // when set, answers wait for it
        public string Anime = "[]", Manga = "[]";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Gate != null) await Gate;
            var body = await req.Content!.ReadAsStringAsync(ct);
            var entries = body.Contains("\"ANIME\"") ? Anime : Manga;
            var json = "{\"data\":{\"MediaListCollection\":{\"hasNextChunk\":" + (AlwaysMore ? "true" : "false") + ",\"lists\":[{\"entries\":" + entries + "}]}}}";
            return new HttpResponseMessage(Status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    static string Entry(int id, string status, int progress = 0, int repeat = 0, string started = "null", string done = "null") =>
        $$"""{"mediaId":{{id}},"status":"{{status}}","progress":{{progress}},"repeat":{{repeat}},"startedAt":{{started}},"completedAt":{{done}}}""";

    static AppDbContext Db(string? name = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);
        db.Users.AddRange(User("a", "alice"), User("b", "Bob"));
        db.Series.Add(new Series { Id = 1, Name = "S" });
        db.Titles.AddRange(
            new Title { Id = 1, AniListId = 100, Name = "A1", SeriesId = 1, Type = TitleType.Anime },
            new Title { Id = 2, AniListId = 101, Name = "A2", SeriesId = 1, Type = TitleType.Anime },
            new Title { Id = 3, AniListId = 102, Name = "A3", SeriesId = 1, Type = TitleType.Anime },
            new Title { Id = 4, AniListId = 200, Name = "M1", SeriesId = 1, Type = TitleType.Manga });
        db.SaveChanges();
        return db;
    }

    static ListImporter Importer(AppDbContext db, Fake f) => new(db, new AniListClient(new HttpClient(f)) { DelayMs = 0 });

    [Fact]
    public async Task Matches_by_anilist_id_maps_status_and_dates_and_defaults_to_only_me()
    {
        using var db = Db();
        db.WatchList.Add(new WatchListEntry { UserId = "a", TitleId = 2, Status = WatchStatus.Dropped, Progress = 1 }); // already there: stays as it is
        await db.SaveChangesAsync();
        var f = new Fake
        {
            Anime = "[" + string.Join(",",
                Entry(100, "COMPLETED", 12, 2, """{"year":2024,"month":1,"day":5}""", """{"year":2024,"month":2,"day":1}"""),
                Entry(101, "CURRENT", 3),
                Entry(102, "PAUSED", 4, 1, """{"year":2024,"month":5,"day":5}""", """{"year":2024,"month":4,"day":1}"""), // finished before started: dates dropped
                Entry(999, "PLANNING"), // not in our database
                Entry(200, "PLANNING")) + "]", // the manga id on the anime list: wrong type, unmatched
            Manga = "[" + Entry(200, "PLANNING", 0) + "]"
        };

        var r = await Importer(db, f).Run("a", "alice_al");

        Assert.Equal(new ListImportResult(Added: 3, AlreadyOnList: 1, Unmatched: 2), r);
        var rows = await db.WatchList.Where(w => w.UserId == "a").OrderBy(w => w.TitleId).ToListAsync();
        Assert.Equal(new[] { WatchStatus.Completed, WatchStatus.Dropped, WatchStatus.Watching, WatchStatus.Planning }, rows.Select(x => x.Status).ToArray());
        Assert.Equal(1, rows[1].Progress); // the existing entry was not touched
        Assert.All(rows.Where(x => x.TitleId != 2), x => Assert.Equal(Audience.OnlyMe, x.Audience));
        Assert.Equal((new DateOnly(2024, 1, 5), new DateOnly(2024, 2, 1), 2, 12), (rows[0].StartedOn, rows[0].FinishedOn, rows[0].RewatchCount, rows[0].Progress));
        Assert.Equal((null, null, 1), (rows[2].StartedOn, rows[2].FinishedOn, rows[2].RewatchCount));
        Assert.Empty(db.WatchList.Where(w => w.UserId == "b")); // only the importing user's list changes

        Assert.Equal(new ListImportResult(0, 4, 2), await Importer(db, f).Run("a", "alice_al")); // running it again adds nothing
    }

    [Fact]
    public async Task Reads_a_bounded_number_of_chunks()
    {
        using var db = Db();
        var f = new Fake { AlwaysMore = true };
        await Importer(db, f).Run("a", "alice_al");
        Assert.Equal(2 * ListImporter.MaxChunks, f.Calls); // anime and manga, 10 chunks each, then it stops although AniList says more
    }

    [Fact]
    public async Task Unknown_or_private_user_is_a_clear_error()
    {
        using var db = Db();
        await Assert.ThrowsAsync<AniListUserNotFound>(() => Importer(db, new Fake { Status = HttpStatusCode.NotFound }).Run("a", "nobody"));
        Assert.Empty(db.WatchList);
    }

    [Fact]
    public async Task Job_runs_one_import_per_user_and_reports_the_result_or_the_error()
    {
        var name = Guid.NewGuid().ToString();
        using var seed = Db(name);
        var gate = new TaskCompletionSource();
        var f = new Fake { Gate = gate.Task, Anime = "[" + Entry(100, "COMPLETED") + "]" };
        var services = new ServiceCollection().AddLogging()
            .AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options))
            .AddScoped(_ => new AniListClient(new HttpClient(f)) { DelayMs = 0 }).AddScoped<ListImporter>().BuildServiceProvider();
        var job = new ListImportJob(services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<ILogger<ListImportJob>>());

        Assert.True(job.TryStart("a", "alice_al", out var done));
        Assert.True(job.Status("a")!.Running);
        Assert.False(job.TryStart("a", "alice_al", out _)); // one at a time per user
        gate.SetResult();
        await done;
        Assert.Equal(new ListImportResult(1, 0, 0), job.Status("a")!.Result);
        Assert.False(job.Status("a")!.Running);
        Assert.Null(job.Status("b"));

        f.Status = HttpStatusCode.NotFound;
        Assert.True(job.TryStart("a", "nobody", out done));
        await done;
        Assert.Contains("not found", job.Status("a")!.Error);
    }

    [Fact]
    public void Job_refuses_when_the_server_is_busy()
    {
        var gate = new TaskCompletionSource();
        var f = new Fake { Gate = gate.Task };
        var services = new ServiceCollection().AddLogging()
            .AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("busy").Options))
            .AddScoped(_ => new AniListClient(new HttpClient(f)) { DelayMs = 0 }).AddScoped<ListImporter>().BuildServiceProvider();
        var job = new ListImportJob(services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<ILogger<ListImportJob>>());

        for (var i = 0; i < ListImportJob.MaxRunning; i++) Assert.True(job.TryStart("u" + i, "name_" + i, out _));
        Assert.False(job.TryStart("late", "name_late", out _));
        gate.SetResult();
    }

    [Fact]
    public void Start_checks_the_name_and_is_rate_limited_per_ten_minutes()
    {
        var c = As(new ListImportController(), "a");
        var job = new ListImportJob(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), null!);
        foreach (var bad in new string?[] { null, "", "a", "has space", "semi;colon", new string('x', 21), "{x}" })
            Assert.Equal(400, Status(c.Start(new AniListImportRequest(bad), job)));

        var attr = typeof(ListImportController).GetMethod(nameof(ListImportController.Start))!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal("anilist-import", attr?.PolicyName);
    }
}
