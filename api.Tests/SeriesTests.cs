using api.Data;
using api.Models;
using api.Services;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

public class SeriesTests
{
    [Theory]
    [InlineData("Attack on Titan Season 2", "Attack on Titan")]
    [InlineData("Mushoku Tensei: Part 2", "Mushoku Tensei")]
    [InlineData("Title: II", "Title")]
    [InlineData("Season 2", "Season 2")] // would strip to empty, so it stays
    public void Strip_removes_season_markers(string name, string expected) =>
        Assert.Equal(expected, SeriesNames.Strip(name));

    static Title T(int id, string name, TitleType type = TitleType.Anime, int year = 2020, bool locked = false) =>
        new() { Id = id, AniListId = id, Name = name, Type = type, Year = year, SeriesLocked = locked, Series = new Series { Kind = type, Name = name } };

    static TitleRelation Rel(int from, int to, string type) => new() { FromAniListId = from, ToAniListId = to, Type = type };

    static async Task<AppDbContext> Db(params object[] rows)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(opts);
        db.AddRange(rows);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Sequels_of_the_same_type_join_but_spin_offs_and_other_types_do_not()
    {
        using var db = await Db(
            T(1, "Show"), T(2, "Show Season 2", year: 2021), T(3, "Show Side Thing", year: 2022),
            T(4, "Show Manga", TitleType.Manga),
            Rel(1, 2, "SEQUEL"), Rel(2, 1, "PREQUEL"), Rel(1, 3, "SPIN_OFF"), Rel(1, 4, "SEQUEL"));

        await new SeriesAssigner(db).Assign();

        var t = await db.Titles.ToDictionaryAsync(x => x.Id);
        Assert.Equal(t[1].SeriesId, t[2].SeriesId);
        Assert.NotEqual(t[1].SeriesId, t[3].SeriesId);
        Assert.NotEqual(t[1].SeriesId, t[4].SeriesId); // anime vs manga never joins
        Assert.Equal(3, await db.Series.CountAsync());
        Assert.Equal("Show", (await db.Series.FindAsync(t[1].SeriesId))!.Name);
        Assert.Equal(new[] { 1, 2 }, new[] { t[1].Position, t[2].Position });
    }

    [Fact]
    public async Task Locked_titles_are_left_alone()
    {
        using var db = await Db(T(1, "Show"), T(2, "Show 2nd Season", locked: true), Rel(1, 2, "SEQUEL"));
        var lockedSeries = (await db.Titles.FindAsync(2))!.SeriesId;

        await new SeriesAssigner(db).Assign();

        Assert.Equal(lockedSeries, (await db.Titles.FindAsync(2))!.SeriesId);
        Assert.NotEqual(lockedSeries, (await db.Titles.FindAsync(1))!.SeriesId);
    }

    [Fact]
    public async Task Save_retrying_survives_a_rating_made_while_the_context_was_open()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var seed = new AppDbContext(opts)) { seed.Add(T(1, "Show")); await seed.SaveChangesAsync(); }

        using var importer = new AppDbContext(opts);
        var title = await importer.Titles.FindAsync(1);
        using (var rater = new AppDbContext(opts)) { (await rater.Titles.FindAsync(1))!.RatingCount++; await rater.SaveChangesAsync(); }

        title!.Position = 7;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => importer.SaveChangesAsync()); // what the importer used to hit
        await importer.SaveRetryingAsync();

        using var check = new AppDbContext(opts);
        var saved = await check.Titles.FindAsync(1);
        Assert.Equal(7, saved!.Position);
        Assert.Equal(1, saved.RatingCount); // the rating made meanwhile is kept
    }
}
