using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// Custom lists. The scenarios take a context factory so ListPostgresTests runs them on the real database too.
public class ListTests
{
    internal static async Task<IActionResult> Call(Func<AppDbContext> open, string? me, Func<ListsController, Task<IActionResult>> f)
    {
        using var db = open();
        return await f(As(new ListsController(db), me));
    }

    static async Task<int> NewList(Func<AppDbContext> open, string me, string name, string? audience = null) =>
        Json(await Call(open, me, c => c.Create(new ListRequest(name, audience)))).GetProperty("id").GetInt32();

    static List<string> Names(IActionResult r) => Status(r) == 404 ? new() : Json(r).EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    // alice's three lists, one per audience, against every viewer (admins get no bypass for lists)
    internal static async Task ViewerRuleOnEveryReadPath(Func<AppDbContext> open)
    {
        await Seed(open);
        var ids = new Dictionary<Audience, int>();
        foreach (var a in Enum.GetValues<Audience>()) ids[a] = await NewList(open, "a", a.ToString(), a.ToString());
        await Call(open, "a", c => c.AddItem(ids[Audience.OnlyMe], new ListItemRequest(1, null)));

        (string? Id, string Role)[] viewers = { ("a", "author"), ("b", "friend"), ("c", "stranger"), (null, "anonymous"), ("d", "admin") };
        foreach (var (viewer, role) in viewers)
            foreach (var a in Enum.GetValues<Audience>())
            {
                var want = role == "author" || (role == "friend" ? a != Audience.OnlyMe : a == Audience.Public);
                var who = $"{role} / {a}";
                Assert.Equal(want ? 200 : 404, Status(await Call(open, viewer, c => c.Get(ids[a]))));
                Assert.True(want == Names(await Call(open, viewer, c => c.OfUser("alice"))).Contains(a.ToString()), "profile: " + who);
            }
    }

    [Fact]
    public Task Viewer_rule_on_every_read_path() => ViewerRuleOnEveryReadPath(Mem());

    // a block either way hides even a public list, and the profile it hangs on
    internal static async Task BlocksHideListsBothWays(Func<AppDbContext> open)
    {
        await Seed(open);
        var pub = await NewList(open, "a", "Open");
        var friends = await NewList(open, "a", "Pals", "Friends");

        using (var db = open()) { db.UserBlocks.Add(new UserBlock { BlockerId = "b", BlockedId = "a" }); await db.SaveChangesAsync(); } // Bob blocked alice
        Assert.Equal(404, Status(await Call(open, "b", c => c.Get(pub))));
        Assert.Equal(404, Status(await Call(open, "b", c => c.Get(friends)))); // a friend, but blocked
        Assert.Equal(404, Status(await Call(open, "a", c => c.OfUser("Bob")))); // and the other way: Bob's profile is gone for alice
        Assert.Empty(Names(await Call(open, "b", c => c.OfUser("alice")))); // the blocker still finds the profile, but no list shows
        Assert.Equal(200, Status(await Call(open, "a", c => c.Get(pub)))); // the owner still sees it
        Assert.Equal(200, Status(await Call(open, "c", c => c.Get(pub)))); // an unrelated viewer too

        using (var db = open()) { db.UserBlocks.RemoveRange(db.UserBlocks); db.UserBlocks.Add(new UserBlock { BlockerId = "a", BlockedId = "c" }); await db.SaveChangesAsync(); }
        Assert.Equal(404, Status(await Call(open, "c", c => c.Get(pub)))); // the owner blocked the viewer
        Assert.Equal(200, Status(await Call(open, "b", c => c.Get(friends))));
    }

    [Fact]
    public Task Blocks_hide_lists_both_ways() => BlocksHideListsBothWays(Mem());

    [Fact]
    public async Task Create_validates_and_caps_the_number_of_lists()
    {
        var open = Mem();
        await Seed(open);
        Assert.Equal(400, Status(await Call(open, "a", c => c.Create(new ListRequest("  ")))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.Create(new ListRequest(new string('x', 61))))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.Create(new ListRequest("Ok", "Everyone")))));

        var made = Json(await Call(open, "a", c => c.Create(new ListRequest(" Favourites ")))); // defaults to Public, name trimmed
        Assert.Equal(("Favourites", "Public"), (made.GetProperty("name").GetString(), made.GetProperty("audience").GetString()));
        for (var i = 1; i < TrackingRules.MaxLists; i++) Assert.Equal(201, Status(await Call(open, "a", c => c.Create(new ListRequest("List " + i)))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.Create(new ListRequest("One too many")))));
        Assert.Equal(201, Status(await Call(open, "b", c => c.Create(new ListRequest("Bob's own count")))));
    }

    [Fact]
    public async Task Items_are_added_once_capped_and_only_by_the_owner()
    {
        var open = Mem();
        await Seed(open);
        var id = await NewList(open, "a", "Mine");

        Assert.Equal(400, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(null, null)))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(1, 1)))));
        Assert.Equal(404, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(99, null)))));
        Assert.Equal(404, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(null, 99)))));
        Assert.Equal(404, Status(await Call(open, "b", c => c.AddItem(id, new ListItemRequest(1, null))))); // not Bob's list

        var item = Json(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(1, null)))).GetProperty("id").GetInt32();
        Assert.Equal(201, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(null, 1)))));
        Assert.Equal(409, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(1, null)))));
        Assert.Equal(409, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(null, 1)))));

        var page = Json(await Call(open, "c", c => c.Get(id)));
        Assert.Equal(new[] { "Title 1", "Show" }, page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());
        Assert.False(page.GetProperty("mine").GetBoolean());

        Assert.Equal(404, Status(await Call(open, "b", c => c.RemoveItem(id, item)))); // someone else's
        Assert.Equal(204, Status(await Call(open, "a", c => c.RemoveItem(id, item))));
        Assert.Equal(404, Status(await Call(open, "a", c => c.RemoveItem(id, item))));

        // fill to the cap straight in the database, then one more is refused
        using (var db = open())
        {
            var start = (await db.Titles.MaxAsync(t => t.Id)) + 1;
            for (var t = 0; t < TrackingRules.MaxItems; t++) db.Titles.Add(new Title { Id = start + t, AniListId = start + t, Name = "T" + t, SeriesId = 1 });
            await db.SaveChangesAsync();
            var count = await db.CustomListItems.CountAsync(i => i.ListId == id);
            db.CustomListItems.AddRange(Enumerable.Range(start, TrackingRules.MaxItems - count).Select(t => new CustomListItem { ListId = id, TitleId = t }));
            await db.SaveChangesAsync();
        }
        Assert.Equal(400, Status(await Call(open, "a", c => c.AddItem(id, new ListItemRequest(2, null)))));
    }

    [Fact]
    public async Task Rename_audience_and_delete_are_owner_only()
    {
        var open = Mem();
        await Seed(open);
        var id = await NewList(open, "a", "Mine");
        await Call(open, "a", c => c.AddItem(id, new ListItemRequest(1, null)));

        Assert.Equal(404, Status(await Call(open, "b", c => c.Update(id, new ListRequest("Stolen")))));
        Assert.Equal(404, Status(await Call(open, "b", c => c.Delete(id))));
        Assert.Equal(400, Status(await Call(open, "a", c => c.Update(id, new ListRequest("", null)))));
        var changed = Json(await Call(open, "a", c => c.Update(id, new ListRequest("Renamed", "OnlyMe"))));
        Assert.Equal(("Renamed", "OnlyMe"), (changed.GetProperty("name").GetString(), changed.GetProperty("audience").GetString()));
        var kept = Json(await Call(open, "a", c => c.Update(id, new ListRequest("Again")))); // no audience: keeps OnlyMe
        Assert.Equal("OnlyMe", kept.GetProperty("audience").GetString());

        Assert.Equal(204, Status(await Call(open, "a", c => c.Delete(id))));
        using var db = open();
        Assert.Empty(db.CustomLists);
    }
}
