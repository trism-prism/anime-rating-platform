using System.Security.Claims;
using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class RatingTests
{
    [Fact]
    public void Overall_is_the_average_rounded_to_two_decimals()
    {
        Assert.Equal(7.5, RatingMath.Overall(8, 7, 7, 8));
        Assert.Equal(8.25, RatingMath.Overall(8, 8, 8, 9));
        Assert.Equal(6.75, RatingMath.Overall(7, 7, 6, 7));
    }

    static RatingsController As(string userId, AppDbContext db) => new(db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "test"))
            }
        }
    };

    [Fact]
    public async Task Ratings_are_scoped_to_the_calling_user()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(opts);
        db.Titles.Add(new Title { Id = 1, AniListId = 1, Name = "Test" });
        db.Users.AddRange(new AppUser { Id = "a", EmailConfirmed = true }, new AppUser { Id = "b", EmailConfirmed = true });
        await db.SaveChangesAsync();

        var body = new RatingRequest(8, 8, 8, 8, "good");
        Assert.IsType<OkObjectResult>(await As("a", db).Put(1, body));
        Assert.IsType<OkObjectResult>(await As("b", db).Put(1, body with { Story = 4 }));
        Assert.Equal(2, await db.Ratings.CountAsync());

        Assert.IsType<NoContentResult>(await As("a", db).Delete(1));
        var left = await db.Ratings.ToListAsync();
        Assert.Single(left);
        Assert.Equal("b", left[0].UserId);

        var mine = Assert.IsType<OkObjectResult>(await As("b", db).Mine());
        Assert.Single((System.Collections.IEnumerable)mine.Value!);
        var none = Assert.IsType<OkObjectResult>(await As("a", db).Mine());
        Assert.Empty((System.Collections.IEnumerable)none.Value!);
    }

    [Fact]
    public async Task Review_over_2000_chars_is_rejected()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(opts);
        db.Titles.Add(new Title { Id = 1, AniListId = 1, Name = "Test" });
        db.Users.Add(new AppUser { Id = "a", EmailConfirmed = true });
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await As("a", db).Put(1, new RatingRequest(8, 8, 8, 8, new string('x', 2001))));
        Assert.IsType<OkObjectResult>(await As("a", db).Put(1, new RatingRequest(8, 8, 8, 8, new string('x', 2000))));
    }

    [Fact]
    public async Task Long_search_is_truncated_not_an_error()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(opts);
        db.Titles.Add(new Title { Id = 1, AniListId = 1, Name = new string('a', 100) });
        await db.SaveChangesAsync();

        // the extra chars are cut off, so the 100-char name still matches
        var res = Assert.IsType<OkObjectResult>(await new TitlesController(db).List(new string('a', 100) + "zzz", null, null));
        Assert.Contains("total = 1", res.Value!.ToString());
    }
}
