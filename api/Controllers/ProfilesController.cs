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

        return Ok(new { userName = user.UserName, joinedAt = user.CreatedAt, friendCount, viewerState = state, requestId });
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
                snippet = r.Text == null || r.Spoiler ? null : r.Text.Length > 140 ? r.Text[..140] + "..." : r.Text
            }),
            nextCursor = more ? rows[^1].Id : (int?)null
        });
    }
}
