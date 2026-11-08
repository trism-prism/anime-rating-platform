using api.Controllers;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Tests;

// GET /series sorts, paging and total, on InMemory (the query shapes are plain LINQ)
public class SeriesBrowseTests
{
    static int _ani;

    // one series with one entry
    static Series S(string name, int ratings = 0, double sum = 0, int? year = null, int pop = 0, string genres = "", string synopsis = "") =>
        new()
        {
            Name = name, Popularity = pop,
            Titles = { new Title { AniListId = ++_ani, Name = name, RatingCount = ratings, RatingSum = sum, Year = year, Genres = genres, Synopsis = synopsis, Position = 1, Format = "TV" } }
        };

    static async Task<SeriesController> Ctl(params Series[] rows)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AddRange(rows);
        await db.SaveChangesAsync();
        return new SeriesController(db);
    }

    static async Task<(string[] Names, int Total, string? Next)> Page(SeriesController c, string? sort, string? cursor = null, int take = 24, string? search = null)
    {
        var ok = Assert.IsType<OkObjectResult>(await c.List(search, null, null, cursor, sort, take));
        var v = ok.Value!;
        var items = ((System.Collections.IEnumerable)v.GetType().GetProperty("items")!.GetValue(v)!).Cast<object>()
            .Select(i => (string)i.GetType().GetProperty("Name")!.GetValue(i)!).ToArray();
        return (items, (int)v.GetType().GetProperty("total")!.GetValue(v)!, (string?)v.GetType().GetProperty("nextCursor")!.GetValue(v));
    }

    [Fact]
    public async Task Popular_is_most_ratings_then_popularity_then_id()
    {
        var c = await Ctl(S("Few", 1, 8, pop: 900), S("Many", 5, 40, pop: 1), S("TieLow", 2, 16, pop: 5), S("TieHigh", 2, 16, pop: 50));
        Assert.Equal(new[] { "Many", "TieHigh", "TieLow", "Few" }, (await Page(c, "popular")).Names);
        Assert.Equal((await Page(c, null)).Names, (await Page(c, "popular")).Names); // popular is the default
    }

    [Fact]
    public async Task Popular_with_no_ratings_falls_back_to_popularity_and_ties_break_by_id_descending()
    {
        var c = await Ctl(S("A", pop: 10), S("B", pop: 10), S("C", pop: 99));
        Assert.Equal(new[] { "C", "B", "A" }, (await Page(c, "popular")).Names);
    }

    [Fact]
    public async Task Top_is_highest_average_and_skips_unrated_series()
    {
        var c = await Ctl(S("Mid", 2, 14), S("Best", 1, 10), S("Unrated"), S("TieMore", 4, 28), S("TieLess", 1, 7));
        var page = await Page(c, "top");
        Assert.Equal(new[] { "Best", "TieMore", "Mid", "TieLess" }, page.Names); // 10, 7 (4 ratings), 7 (2), 7 (1): ties by rating count
        Assert.Equal(4, page.Total); // total counts what the sort shows
    }

    [Fact]
    public async Task Newest_is_latest_year_with_unknown_years_last()
    {
        var c = await Ctl(S("Old", year: 1998), S("Unknown"), S("New", year: 2025), S("Mid", year: 2010));
        Assert.Equal(new[] { "New", "Mid", "Old", "Unknown" }, (await Page(c, "newest")).Names);
    }

    [Fact]
    public async Task Newest_uses_the_latest_year_among_a_series_entries()
    {
        var withSequel = S("Show", year: 2000);
        withSequel.Titles.Add(new Title { AniListId = ++_ani, Name = "Show 2", Year = 2024, Position = 2, Format = "TV" });
        var c = await Ctl(S("Other", year: 2020), withSequel);
        Assert.Equal(new[] { "Show", "Other" }, (await Page(c, "newest")).Names);
    }

    [Fact]
    public async Task AZ_is_by_name_with_id_breaking_ties()
    {
        var c = await Ctl(S("Charlie"), S("Alpha"), S("Bravo"), S("Alpha"));
        Assert.Equal(new[] { "Alpha", "Alpha", "Bravo", "Charlie" }, (await Page(c, "az")).Names);
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("top")]
    [InlineData("newest")]
    [InlineData("az")]
    public async Task Pages_cover_every_series_once_in_the_same_order_as_one_big_page(string sort)
    {
        // many ties on purpose: same ratings, same year, same name
        var rows = Enumerable.Range(0, 7).Select(i => S(i % 2 == 0 ? "Same" : "Other", 2, 14, 2020, 5)).ToArray();
        var c = await Ctl(rows);
        var all = await Page(c, sort, take: 100);
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var p = await Page(c, sort, cursor, take: 3);
            seen.AddRange(p.Names);
            cursor = p.Next;
            pages++;
        } while (cursor != null);
        Assert.Equal(3, pages);
        Assert.Equal(7, all.Total);
        Assert.Equal(all.Names, seen);
    }

    [Fact]
    public async Task Total_counts_the_filter_and_a_bad_cursor_or_unknown_sort_is_handled()
    {
        var c = await Ctl(S("Naruto"), S("Bleach"), S("Naruto Shippuden"));
        var found = await Page(c, "az", search: "naru");
        Assert.Equal(2, found.Total);
        Assert.Equal(new[] { "Naruto", "Naruto Shippuden" }, found.Names);
        Assert.IsType<BadRequestObjectResult>(await c.List(null, null, null, "!!", null, 24));
        Assert.Equal(3, (await Page(c, "bogus")).Names.Length); // unknown sort = popular
    }

    [Fact]
    public async Task Detail_carries_the_lead_synopsis_format_and_all_genres()
    {
        var s = S("Show", genres: "Action,Drama", synopsis: "Lead text");
        s.Titles.Add(new Title { AniListId = ++_ani, Name = "Show 2", Position = 2, Format = "MOVIE", Genres = "Drama,Comedy", Synopsis = "Second text" });
        var c = await Ctl(s);
        var id = (await Page(c, "az")).Names.Length; // one series, id 1 on a fresh store
        var ok = Assert.IsType<OkObjectResult>(await c.Get(id));
        var v = ok.Value!;
        Assert.Equal("Lead text", (string?)v.GetType().GetProperty("synopsis")!.GetValue(v));
        Assert.Equal("TV", (string?)v.GetType().GetProperty("format")!.GetValue(v));
        Assert.Equal(new[] { "Action", "Drama", "Comedy" }, (List<string>)v.GetType().GetProperty("genres")!.GetValue(v)!);
    }
}
