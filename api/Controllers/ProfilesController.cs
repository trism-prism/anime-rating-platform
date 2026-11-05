using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[AllowAnonymous]
public class ProfilesController : ControllerBase
{
    readonly AppDbContext _db;

    public ProfilesController(AppDbContext db) => _db = db;

    // Public part for everyone; signed-in viewers also get their relationship to this user.
    // No email, no user ids, and nothing the user has rated or listed (that arrives with audiences).
    [HttpGet("users/{username}")]
    public async Task<IActionResult> Get(string username)
    {
        // AllowAnonymous skips the 401 an expired token would get; send it so the web client refreshes and retries
        // instead of showing a signed-in user the anonymous view
        if (Request.Headers.ContainsKey("Authorization") && User.Identity?.IsAuthenticated != true) return Unauthorized();

        var user = await SocialRules.FindByName(_db, username);
        var viewer = User.FindFirst("sub")?.Value;
        // someone who blocked the viewer looks like a missing account
        if (user == null || (viewer != null && viewer != user.Id && await SocialRules.HasBlocked(_db, user.Id, viewer)))
            return NotFound();

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
}
