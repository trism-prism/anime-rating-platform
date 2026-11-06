using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[Authorize]
[Route("me")]
public class FeedController : ControllerBase
{
    const int PageSize = 20;
    readonly AppDbContext _db;

    public FeedController(AppDbContext db) => _db = db;

    // Recent ratings and reviews by the viewer's accepted friends, newest first, keyset by rating id (pass nextCursor back as ?cursor=).
    // The viewer rule decides what shows (Only me stays hidden, a block hides both ways); admins get no extra view here.
    [HttpGet("feed")]
    public async Task<IActionResult> Get(int? cursor)
    {
        var me = User.FindFirst("sub")!.Value;
        var q = _db.Ratings
            .Where(r => _db.Friendships.Any(f => f.Status == FriendStatus.Accepted
                && ((f.UserLowId == r.UserId && f.UserHighId == me) || (f.UserLowId == me && f.UserHighId == r.UserId))))
            .VisibleTo(_db, me, false);
        if (cursor != null) q = q.Where(r => r.Id < cursor);
        var rows = await q.OrderByDescending(r => r.Id).Take(PageSize + 1)
            .Select(r => new
            {
                r.Id, userName = r.User.UserName, r.TitleId, titleName = r.Title.Name, r.Volume, r.Overall, r.Spoiler, r.CreatedAt, Text = r.ReviewText
            }).ToListAsync();
        var more = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();
        return Ok(new
        {
            items = rows.Select(r => new
            {
                ratingId = r.Id, r.userName, r.TitleId, r.titleName, r.Volume, r.Overall, r.CreatedAt,
                hasReview = r.Text != null,
                r.Spoiler,
                // a spoiler review sends no snippet at all, like the profile list
                snippet = r.Text == null || r.Spoiler ? null : r.Text.Length > 140 ? r.Text[..140] + "..." : r.Text
            }),
            nextCursor = more ? rows[^1].Id : (int?)null
        });
    }
}
