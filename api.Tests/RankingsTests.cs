using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class RankingsTests
{
    [Fact]
    public async Task Rankings_are_ordered_and_need_three_ratings()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(opts);
        db.Titles.AddRange(
            new Title { Id = 1, AniListId = 1, Name = "Good" },
            new Title { Id = 2, AniListId = 2, Name = "Best but few votes" },
            new Title { Id = 3, AniListId = 3, Name = "Better" });
        foreach (var (title, score, votes) in new[] { (1, 7, 3), (2, 10, 2), (3, 9, 4) })
            for (var i = 0; i < votes; i++)
                db.Ratings.Add(new Rating { TitleId = title, UserId = "u" + i, Story = score, Art = 1, Characters = 1, Enjoyment = 1, Overall = score });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await new RankingsController(db).Get("story"));
        var names = ((IEnumerable<object>)ok.Value!).Select(x => x.GetType().GetProperty("Name")!.GetValue(x)).ToList();

        Assert.Equal(new object[] { "Better", "Good" }, names);
        Assert.IsType<BadRequestObjectResult>(await new RankingsController(db).Get("nope"));
    }
}
