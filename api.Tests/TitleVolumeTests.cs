using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class TitleVolumeTests
{
    [Fact]
    public async Task Volume_averages_show_whenever_volume_ratings_exist()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AppDbContext(opts);
        // the volume count was later lost (AniList reset it), but a volume was already rated
        db.Titles.Add(new Title { Id = 1, AniListId = 1, Name = "Manga", Type = TitleType.Manga, Volumes = null, Series = new Series { Name = "Manga" } });
        db.Ratings.Add(new Rating { TitleId = 1, UserId = "a", Volume = 2, Story = 8, Art = 8, Characters = 8, Enjoyment = 8, Overall = 8 });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await new TitlesController(db).Get(1));
        var averages = (System.Collections.IEnumerable?)ok.Value!.GetType().GetProperty("volumeAverages")!.GetValue(ok.Value);
        Assert.NotNull(averages);
        Assert.Single(averages!.Cast<object>());
    }
}
