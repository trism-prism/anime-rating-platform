using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace api.Controllers;

public record ListRequest(string? Name, string? Audience = null);
public record ListItemRequest(int? TitleId, int? SeriesId);

// Custom lists: up to 20 per user, 500 items each. Reads apply the viewer rule (audience and blocks); a hidden list is the same
// 404 as a missing one.
[ApiController]
[Authorize]
public class ListsController : ControllerBase
{
    readonly AppDbContext _db;

    public ListsController(AppDbContext db) => _db = db;

    string Me => User.FindFirst("sub")!.Value;

    [HttpGet("me/lists")]
    public async Task<IActionResult> Mine()
    {
        var me = Me;
        var lists = await _db.CustomLists.Where(l => l.UserId == me).OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.Name, Audience = l.Audience.ToString(), itemCount = l.Items.Count }).ToListAsync();
        return Ok(lists);
    }

    [HttpPost("me/lists")]
    [EnableRateLimiting("list-write")]
    public async Task<IActionResult> Create(ListRequest req)
    {
        var name = Clean(req.Name);
        if (name == null) return BadRequest(new { message = $"Give the list a name of up to {TrackingRules.MaxListName} characters." });
        var audience = Audience.Public;
        if (req.Audience != null && !ReviewRules.TryAudience(req.Audience, out audience))
            return BadRequest(new { message = "Audience must be Public, Friends or OnlyMe." });
        var me = Me;
        if (await _db.CustomLists.CountAsync(l => l.UserId == me) >= TrackingRules.MaxLists)
            return BadRequest(new { message = $"You can have up to {TrackingRules.MaxLists} lists." });
        var list = new CustomList { UserId = me, Name = name, Audience = audience };
        _db.CustomLists.Add(list);
        await _db.SaveChangesAsync();
        return StatusCode(201, new { list.Id, list.Name, audience = list.Audience.ToString(), itemCount = 0 });
    }

    // rename and/or change the audience; a missing audience keeps the current one
    [HttpPut("me/lists/{id:int}")]
    [EnableRateLimiting("list-write")]
    public async Task<IActionResult> Update(int id, ListRequest req)
    {
        var name = Clean(req.Name);
        if (name == null) return BadRequest(new { message = $"Give the list a name of up to {TrackingRules.MaxListName} characters." });
        Audience? audience = null;
        if (req.Audience != null)
        {
            if (!ReviewRules.TryAudience(req.Audience, out var a)) return BadRequest(new { message = "Audience must be Public, Friends or OnlyMe." });
            audience = a;
        }
        var me = Me;
        var list = await _db.CustomLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == me);
        if (list == null) return NotFound();
        list.Name = name;
        if (audience != null) list.Audience = audience.Value;
        await _db.SaveChangesAsync();
        return Ok(new { list.Id, list.Name, audience = list.Audience.ToString() });
    }

    [HttpDelete("me/lists/{id:int}")]
    [EnableRateLimiting("list-write")]
    public async Task<IActionResult> Delete(int id)
    {
        var me = Me;
        var list = await _db.CustomLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == me);
        if (list == null) return NotFound();
        _db.CustomLists.Remove(list); // its items go with it (cascade)
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("me/lists/{id:int}/items")]
    [EnableRateLimiting("list-write")]
    public async Task<IActionResult> AddItem(int id, ListItemRequest req)
    {
        if ((req.TitleId == null) == (req.SeriesId == null)) return BadRequest(new { message = "Give either a titleId or a seriesId." });
        var me = Me;
        if (!await _db.CustomLists.AnyAsync(l => l.Id == id && l.UserId == me)) return NotFound();
        if (req.TitleId != null ? !await _db.Titles.AnyAsync(t => t.Id == req.TitleId) : !await _db.Series.AnyAsync(s => s.Id == req.SeriesId))
            return NotFound();
        if (await _db.CustomListItems.AnyAsync(i => i.ListId == id && i.TitleId == req.TitleId && i.SeriesId == req.SeriesId))
            return Conflict(new { message = "Already on this list." });
        // ponytail: count-then-insert, two parallel adds could pass 500 by one or two; add a lock if that ever matters
        if (await _db.CustomListItems.CountAsync(i => i.ListId == id) >= TrackingRules.MaxItems)
            return BadRequest(new { message = $"A list holds up to {TrackingRules.MaxItems} items." });

        var item = new CustomListItem { ListId = id, TitleId = req.TitleId, SeriesId = req.SeriesId };
        _db.CustomListItems.Add(item);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear(); // a double click added it first
            return Conflict(new { message = "Already on this list." });
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            _db.ChangeTracker.Clear(); // the title, series or list went meanwhile
            return NotFound();
        }
        return StatusCode(201, new { item.Id });
    }

    [HttpDelete("me/lists/{id:int}/items/{itemId:int}")]
    [EnableRateLimiting("list-write")]
    public async Task<IActionResult> RemoveItem(int id, int itemId)
    {
        var me = Me;
        var item = await _db.CustomListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ListId == id && i.List.UserId == me);
        if (item == null) return NotFound();
        _db.CustomListItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // The list page. Same 404 for unknown, hidden by audience and blocked.
    [AllowAnonymous]
    [EnableRateLimiting("profile")]
    [HttpGet("lists/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = User.FindFirst("sub")?.Value;
        var list = await _db.CustomLists.Where(l => l.Id == id).VisibleTo(_db, viewer)
            .Select(l => new { l.Id, l.Name, Audience = l.Audience.ToString(), userName = l.User.UserName, mine = l.UserId == viewer }).FirstOrDefaultAsync();
        if (list == null) return NotFound();

        var items = await _db.CustomListItems.Where(i => i.ListId == id).OrderBy(i => i.Id)
            .Select(i => new
            {
                itemId = i.Id, titleId = i.TitleId, seriesId = i.TitleId != null ? i.Title!.SeriesId : i.SeriesId,
                name = i.TitleId != null ? i.Title!.Name : i.Series!.Name,
                coverUrl = i.TitleId != null ? i.Title!.CoverUrl : i.Series!.CoverUrl
            }).ToListAsync();
        return Ok(new { list.Id, list.Name, audience = list.Audience, list.userName, list.mine, items });
    }

    // the lists on a profile: only the ones the viewer may see
    [AllowAnonymous]
    [EnableRateLimiting("profile")]
    [HttpGet("users/{username}/lists")]
    public async Task<IActionResult> OfUser(string username)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = User.FindFirst("sub")?.Value;
        var user = await SocialRules.FindVisible(_db, viewer, username);
        if (user == null) return NotFound();
        var lists = await _db.CustomLists.Where(l => l.UserId == user.Id).VisibleTo(_db, viewer).OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.Name, itemCount = l.Items.Count }).ToListAsync();
        return Ok(lists);
    }

    static string? Clean(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrEmpty(name) || name.Length > TrackingRules.MaxListName ? null : name;
    }
}
