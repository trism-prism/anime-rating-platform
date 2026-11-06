using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static api.Tests.SocialTests;

namespace api.Tests;

public class ExportTests
{
    const string Pass = "correct horse battery";

    internal static async Task<(ExportController Alice, AppDbContext Db)> Setup(Action<DbContextOptionsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDbContext<AppDbContext>(o => (configure ?? (b => b.UseInMemoryDatabase(Guid.NewGuid().ToString())))(o));
        services.AddIdentityCore<AppUser>(IdentitySetup.Configure).AddEntityFrameworkStores<AppDbContext>();
        var p = services.BuildServiceProvider();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        foreach (var (id, name) in new[] { ("a", "alice"), ("b", "bob") })
            Assert.True((await users.CreateAsync(new AppUser { Id = id, UserName = name, Email = name + "@example.com" }, Pass)).Succeeded);

        db.Series.Add(new Series { Id = 1, Name = "Show" });
        db.Titles.AddRange(new Title { Id = 1, AniListId = 1, Name = "=Evil, \"Title\"", SeriesId = 1 }, new Title { Id = 2, AniListId = 2, Name = "Plain", SeriesId = 1 });
        await db.SaveChangesAsync();
        db.Ratings.AddRange(
            new Rating { UserId = "a", TitleId = 1, Story = 8, Art = 8, Characters = 8, Enjoyment = 8, Overall = 8, ReviewText = "great, really", Audience = Audience.OnlyMe },
            new Rating { UserId = "b", TitleId = 1, Story = 1, Art = 1, Characters = 1, Enjoyment = 1, Overall = 1, ReviewText = "BOB SECRET REVIEW" });
        db.WatchList.AddRange(
            new WatchListEntry { UserId = "a", TitleId = 2, Status = WatchStatus.Completed, Progress = 12, StartedOn = new DateOnly(2025, 1, 2), RewatchCount = 1 },
            new WatchListEntry { UserId = "b", TitleId = 2, Status = WatchStatus.Dropped, Progress = 99 });
        var list = new CustomList { UserId = "a", Name = "Faves" };
        db.CustomLists.AddRange(list, new CustomList { UserId = "b", Name = "BOB LIST" });
        await db.SaveChangesAsync();
        db.CustomListItems.AddRange(new CustomListItem { ListId = list.Id, TitleId = 2 }, new CustomListItem { ListId = list.Id + 1, TitleId = 1 });
        await db.SaveChangesAsync();
        return (As(new ExportController(db, users, new CredentialCheck(users)), "a"), db);
    }

    static byte[] Body(IActionResult r) => ((FileContentResult)r).FileContents;

    [Fact]
    public async Task Json_holds_only_the_callers_data_with_dates() => await AssertJson((await Setup()).Alice);

    internal static async Task AssertJson(ExportController c)
    {
        var r = await c.Export(new ExportRequest(Pass, "json"));
        var text = Encoding.UTF8.GetString(Body(r));
        Assert.DoesNotContain("BOB", text);
        Assert.DoesNotContain("SECRET", text);
        var doc = JsonDocument.Parse(text).RootElement;
        Assert.Equal(1, doc.GetProperty("ratings").GetArrayLength());
        Assert.Equal("great, really", doc.GetProperty("reviews")[0].GetProperty("text").GetString());
        var e = doc.GetProperty("listEntries")[0];
        Assert.Equal(("2025-01-02", 1, 12), (e.GetProperty("startedOn").GetString(), e.GetProperty("rewatchCount").GetInt32(), e.GetProperty("progress").GetInt32()));
        Assert.Equal("Faves", doc.GetProperty("customLists")[0].GetProperty("name").GetString());
        Assert.Equal("Plain", doc.GetProperty("customListItems")[0].GetProperty("name").GetString());
        Assert.NotEqual(default, doc.GetProperty("ratings")[0].GetProperty("createdAt").GetDateTime());
    }

    [Fact]
    public async Task Csv_is_a_zip_of_tables_with_quoting_and_formula_protection() => await AssertCsv((await Setup()).Alice);

    internal static async Task AssertCsv(ExportController c)
    {
        var r = await c.Export(new ExportRequest(Pass, "CSV"));
        using var zip = new ZipArchive(new MemoryStream(Body(r)));
        Assert.Equal(new[] { "custom-list-items.csv", "custom-lists.csv", "list-entries.csv", "ratings.csv", "reviews.csv" }, zip.Entries.Select(x => x.Name).Order().ToArray());
        string Read(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();

        var ratings = Read("ratings.csv");
        Assert.StartsWith("﻿Title,Volume,Story,Art,Characters,Enjoyment,Overall,Audience,CreatedAt\r\n", ratings);
        Assert.Contains("\"'=Evil, \"\"Title\"\"\",,8,8,8,8,8,OnlyMe,", ratings); // quoted, doubled quotes, formula defused
        Assert.Contains("Plain,Anime,Completed,12,Public,2025-01-02,,1", Read("list-entries.csv"));
        Assert.Contains("\"great, really\",false,OnlyMe", Read("reviews.csv"));
        foreach (var e in zip.Entries) Assert.DoesNotContain("BOB", new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public async Task Needs_the_current_password_and_a_known_format()
    {
        var (c, _) = await Setup();
        Assert.Equal(403, Status(await c.Export(new ExportRequest("wrong password!!", "json"))));
        Assert.Equal(403, Status(await c.Export(new ExportRequest(null, "json"))));
        Assert.Equal(400, Status(await c.Export(new ExportRequest(Pass, "xml"))));
        Assert.Equal(400, Status(await c.Export(new ExportRequest(Pass, null))));
    }

    [Fact]
    public void Export_is_rate_limited()
    {
        var attr = typeof(ExportController).GetMethod(nameof(ExportController.Export))!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal("export", attr?.PolicyName);
    }
}
