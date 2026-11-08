using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[AllowAnonymous]
public class ProfilesController : ControllerBase
{
    const int RatingsPage = 20;
    readonly AppDbContext _db;

    public ProfilesController(AppDbContext db) => _db = db;

    // Public part for everyone; signed-in viewers also get their relationship to this user.
    // No email and no user ids; ratings come from users/{username}/ratings, which applies each rating's audience.
    [EnableRateLimiting("profile")]
    [HttpGet("users/{username}")]
    public async Task<IActionResult> Get(string username)
    {
        // AllowAnonymous skips the 401 an expired token would get; send it so the web client refreshes and retries
        // instead of showing a signed-in user the anonymous view
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();

        var viewer = User.FindFirst("sub")?.Value;
        // someone who blocked the viewer looks like a missing account (one query either way)
        var user = await SocialRules.FindVisible(_db, viewer, username);
        if (user == null) return NotFound();

        var friendCount = await _db.Friendships.CountAsync(f => f.Status == FriendStatus.Accepted && (f.UserLowId == user.Id || f.UserHighId == user.Id));

        string? state = null;
        int? requestId = null;
        if (viewer != null)
        {
            state = "None";
            if (viewer == user.Id) state = "Self";
            else if (await SocialRules.HasBlocked(_db, viewer, user.Id)) state = "BlockedByViewer";
            else if (await SocialRules.FindFriendship(_db, viewer, user.Id) is { } f)
            {
                state = f.Status == FriendStatus.Accepted ? "Friends" : f.RequesterId == viewer ? "OutgoingPending" : "IncomingPending";
                if (f.Status == FriendStatus.Pending) requestId = f.Id; // the viewer needs it to cancel, accept or decline
            }
        }

        var hasAvatar = await _db.Avatars.AnyAsync(a => a.UserId == user.Id);
        return Ok(new { userName = user.UserName, joinedAt = user.CreatedAt, friendCount, viewerState = state, requestId, bio = user.Bio, hasAvatar });
    }

    // The picture as an image (public, like the profile; an <img> can't send a token so blocks don't apply). 204 when there is none
    // or the account is gone (a 404 would log an error in the browser console for every such avatar): the web client then shows the initial-letter avatar. no-cache + ETag: always revalidated, a 304 costs no bytes.
    // The bytes are only read when the ETag doesn't match.
    [EnableRateLimiting("avatar-read")]
    [HttpGet("users/{username}/avatar")]
    public async Task<IActionResult> Avatar(string username)
    {
        if (!SocialRules.ValidUserName(username)) return NotFound();
        var normalized = username.ToUpperInvariant();
        var meta = await _db.Avatars.Where(x => _db.Users.Any(u => u.Id == x.UserId && u.NormalizedUserName == normalized && u.DeletedAt == null))
            .Select(x => new { x.UserId, x.ContentType, x.UpdatedAt }).FirstOrDefaultAsync();
        if (meta == null) return NoContent();

        var etag = $"\"{meta.UpdatedAt.Ticks}\"";
        Response.Headers.CacheControl = "public, no-cache";
        Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.Contains(etag)) return StatusCode(304);
        var data = await _db.Avatars.Where(x => x.UserId == meta.UserId).Select(x => x.Data).FirstOrDefaultAsync();
        return data == null ? NoContent() : File(data, meta.ContentType);
    }

    // keyset by rating id, newest first, 20 per page. The viewer rule decides which ratings show (a block either way shows none);
    // the name lookup is the same one the profile uses, so an unknown name and a user who blocked the viewer are one 404.
    [EnableRateLimiting("profile")]
    [HttpGet("users/{username}/ratings")]
    public async Task<IActionResult> Ratings(string username, int? cursor)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = User.FindFirst("sub")?.Value;
        var user = await SocialRules.FindVisible(_db, viewer, username);
        if (user == null) return NotFound();

        var admin = await ReviewRules.IsAdmin(_db, viewer);
        var q = _db.Ratings.Where(r => r.UserId == user.Id).VisibleTo(_db, viewer, admin);
        if (cursor != null) q = q.Where(r => r.Id < cursor);
        var rows = await q.OrderByDescending(r => r.Id).Take(RatingsPage + 1)
            .Select(r => new
            {
                r.Id, r.TitleId, titleName = r.Title.Name, r.Title.SeriesId, seriesName = r.Title.Series.Name, r.Volume,
                r.Overall, r.Spoiler, r.CreatedAt, Text = r.ReviewText
            }).ToListAsync();
        var more = rows.Count > RatingsPage;
        rows = rows.Take(RatingsPage).ToList();
        return Ok(new
        {
            items = rows.Select(r => new
            {
                ratingId = r.Id, r.TitleId, r.titleName, r.SeriesId, r.seriesName, r.Volume, r.Overall, r.CreatedAt,
                hasReview = r.Text != null,
                r.Spoiler,
                // a spoiler review sends no snippet at all, so the list can't give it away
                snippet = ReviewRules.Snippet(r.Text, r.Spoiler)
            }),
            nextCursor = more ? rows[^1].Id : (int?)null
        });
    }

    // Top 5 titles, top 3 genres by average score and the rating count, from the ratings this viewer may see (same rule and
    // same 404 as the profile). Whole-title ratings only for the top lists; the count includes volume ratings.
    // Genres need 2+ rated titles; if fewer than 3 qualify the rest are filled from the thinner ones so the section isn't empty.
    [EnableRateLimiting("profile-overview")]
    [HttpGet("users/{username}/overview")]
    public async Task<IActionResult> Overview(string username)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = User.FindFirst("sub")?.Value;
        var user = await SocialRules.FindVisible(_db, viewer, username);
        if (user == null) return NotFound();

        var admin = await ReviewRules.IsAdmin(_db, viewer);
        var q = _db.Ratings.Where(r => r.UserId == user.Id).VisibleTo(_db, viewer, admin);
        var ratingCount = await q.CountAsync();
        // a user's ratings are few enough to group in memory, like the stats page; the review text is not loaded for all of them
        var whole = await q.Where(r => r.Volume == null)
            .Select(r => new
            {
                r.Id, r.TitleId, titleName = r.Title.Name, r.Title.SeriesId, r.Title.CoverUrl, r.Title.Genres,
                r.Overall, r.Spoiler, r.CreatedAt, HasText = r.ReviewText != null
            }).ToListAsync();

        var best = whole.OrderByDescending(r => r.Overall).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(5).ToList();
        // text only for the five, and only enough of it for a snippet
        var ids = best.Where(r => r.HasText && !r.Spoiler).Select(r => r.Id).ToList();
        var texts = await _db.Ratings.Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, Text = r.ReviewText!.Length > 141 ? r.ReviewText.Substring(0, 141) : r.ReviewText }).ToDictionaryAsync(x => x.Id, x => x.Text);
        var top = best.Select(r => new
        {
            ratingId = r.Id, r.TitleId, r.titleName, r.SeriesId, r.CoverUrl, r.Overall,
            hasReview = r.HasText,
            r.Spoiler,
            snippet = texts.TryGetValue(r.Id, out var t) ? ReviewRules.Snippet(t, false) : null
        });

        var genres = whole
            .SelectMany(r => r.Genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(g => (g, r.Overall)))
            .GroupBy(x => x.g)
            .Select(g => new { name = g.Key, avg = g.Average(x => x.Overall), count = g.Count() })
            .OrderByDescending(g => g.count >= 2).ThenByDescending(g => g.avg).ThenByDescending(g => g.count).ThenBy(g => g.name)
            .Take(3).Select(g => new { g.name, average = Math.Round(g.avg, 2), g.count }).ToList();

        return Ok(new { ratingCount, topTitles = top, topGenres = genres });
    }
}
