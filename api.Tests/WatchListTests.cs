using System.Security.Claims;
using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class WatchListTests
{
    static WatchListController As(AppDbContext db, string userId) => new(db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "test")) }
        }
    };

    static int Count(IActionResult result) => ((System.Collections.IEnumerable)((OkObjectResult)result).Value!).Cast<object>().Count();

    [Fact]
    public async Task Entries_are_scoped_to_the_caller()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(options);
        db.Titles.Add(new Title { Id = 1, Name = "Frieren", Series = new Series { Name = "Frieren" } });
        await db.SaveChangesAsync();

        var a = As(db, "user-a");
        var b = As(db, "user-b");
        Assert.IsType<OkObjectResult>(await a.Put(1, new WatchListRequest("Watching", 3)));
        Assert.IsType<OkObjectResult>(await b.Put(1, new WatchListRequest("Planning", 0)));
        Assert.Equal(1, Count(await a.Get()));

        Assert.IsType<NoContentResult>(await a.Delete(1));
        Assert.Equal(0, Count(await a.Get()));
        Assert.Equal(1, Count(await b.Get()));
        Assert.IsType<NotFoundResult>(await a.Delete(1));
    }

    [Fact]
    public async Task Put_rejects_bad_input()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(options);
        db.Titles.Add(new Title { Id = 1, Name = "Frieren", Series = new Series { Name = "Frieren" } });
        await db.SaveChangesAsync();
        var a = As(db, "user-a");

        Assert.IsType<BadRequestObjectResult>(await a.Put(1, new WatchListRequest("Nope", 0)));
        Assert.IsType<BadRequestObjectResult>(await a.Put(1, new WatchListRequest("1", 0)));
        Assert.IsType<BadRequestObjectResult>(await a.Put(1, new WatchListRequest("Watching", -1)));
        Assert.IsType<NotFoundResult>(await a.Put(99, new WatchListRequest("Watching", 1)));
    }

    [Fact]
    public async Task Dates_are_validated_and_stored_per_user()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(options);
        db.Titles.Add(new Title { Id = 1, Name = "Frieren", Series = new Series { Name = "Frieren" } });
        await db.SaveChangesAsync();
        var a = As(db, "user-a");
        var d = (int y, int m, int day) => new DateOnly(y, m, day);

        Assert.IsType<NotFoundResult>(await a.SetDates(1, new WatchDatesRequest(d(2025, 1, 1), null, 0))); // not on the list yet
        await a.Put(1, new WatchListRequest("Completed", 12));
        Assert.IsType<BadRequestObjectResult>(await a.SetDates(1, new WatchDatesRequest(d(2025, 2, 1), d(2025, 1, 1), 0))); // finished before started
        Assert.IsType<BadRequestObjectResult>(await a.SetDates(1, new WatchDatesRequest(null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), 0))); // future
        Assert.IsType<BadRequestObjectResult>(await a.SetDates(1, new WatchDatesRequest(null, null, -1)));
        Assert.IsType<BadRequestObjectResult>(await a.SetDates(1, new WatchDatesRequest(null, null, 5000)));

        Assert.IsType<OkObjectResult>(await a.SetDates(1, new WatchDatesRequest(d(2025, 1, 1), d(2025, 1, 1), 2))); // same day is fine
        var e = await db.WatchList.SingleAsync();
        Assert.Equal((d(2025, 1, 1), d(2025, 1, 1), 2), (e.StartedOn, e.FinishedOn, e.RewatchCount));

        await a.Put(1, new WatchListRequest("Watching", 3)); // a status change keeps the dates
        Assert.Equal(2, (await db.WatchList.SingleAsync()).RewatchCount);
        Assert.IsType<NotFoundResult>(await As(db, "user-b").SetDates(1, new WatchDatesRequest(null, null, 1))); // someone else's entry
    }
}
