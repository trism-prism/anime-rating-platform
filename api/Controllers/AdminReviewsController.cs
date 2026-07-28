using api.Data;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

// Minimal moderation until reports exist. Records nothing: the review's text goes, its score stays.
[ApiController]
[Authorize]
[AdminOnly]
[Route("admin")]
public class AdminReviewsController : ControllerBase
{
    readonly AppDbContext _db;

    public AdminReviewsController(AppDbContext db) => _db = db;

    [HttpDelete("reviews/{id:int}/text")]
    public async Task<IActionResult> RemoveText(int id)
    {
        var r = await _db.Ratings.FirstOrDefaultAsync(x => x.Id == id && x.ReviewText != null);
        if (r == null) return NotFound();
        r.ReviewText = null;
        r.Spoiler = false;
        await SocialRules.RemoveReviewNotifications(_db, id); // they would point at a review that is gone
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
    }

    [HttpDelete("replies/{id:int}")]
    public async Task<IActionResult> DeleteReply(int id)
    {
        var reply = await _db.Replies.FirstOrDefaultAsync(x => x.Id == id);
        if (reply == null) return NotFound();
        _db.Replies.Remove(reply);
        await SocialRules.RemoveReplyNotifications(_db, new[] { id });
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
    }
}
