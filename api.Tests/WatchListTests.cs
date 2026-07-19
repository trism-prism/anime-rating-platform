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
}
