using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record SendFriendRequest(string? Username);

[ApiController]
[Authorize]
public class FriendsController : ControllerBase
{
    readonly AppDbContext _db;

    public FriendsController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the plain "sub" claim; ids never come from the body or the route
    string UserId => User.FindFirst("sub")!.Value;

    // accepted friends plus pending requests in both directions
    [HttpGet("me/friends")]
    public async Task<IActionResult> List()
    {
        var me = UserId;
        var rows = await _db.Friendships.Where(f => f.UserLowId == me || f.UserHighId == me).OrderByDescending(f => f.Id).ToListAsync();
        var ids = rows.Select(f => f.OtherId(me)).ToList();
        var names = await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName!);

        return Ok(new
        {
            friends = rows.Where(f => f.Status == FriendStatus.Accepted)
                .Select(f => new { userName = names[f.OtherId(me)], since = f.RespondedAt ?? f.CreatedAt }),
            incoming = rows.Where(f => f.Status == FriendStatus.Pending && f.RequesterId != me)
                .Select(f => new { requestId = f.Id, userName = names[f.RequesterId], sentAt = f.CreatedAt }),
            outgoing = rows.Where(f => f.Status == FriendStatus.Pending && f.RequesterId == me)
                .Select(f => new { requestId = f.Id, userName = names[f.OtherId(me)], sentAt = f.CreatedAt })
        });
    }

    // by exact username. If they already asked me, this accepts. Someone who blocked me gets the same answer as an unknown name.
    [HttpPost("friends/requests")]
    [EnableRateLimiting("friend-request")]
    public async Task<IActionResult> Send(SendFriendRequest req)
    {
        var me = UserId;
        var target = await SocialRules.FindVisible(_db, me, req.Username);
        if (target == null)
            return NotFound(new { message = "No user with that username." });
        if (target.Id == me) return BadRequest(new { message = "You can't add yourself." });
        if (await SocialRules.HasBlocked(_db, me, target.Id)) return Conflict(new { message = "You blocked this user. Unblock them first." });

        var f = await SocialRules.FindFriendship(_db, me, target.Id);
        if (f != null)
        {
            if (f.Status == FriendStatus.Accepted) return Conflict(new { message = "You are already friends." });
            if (f.RequesterId == me) return Conflict(new { message = "You already sent this user a request." });
            await AcceptRow(f, me);
        }
        else
        {
            var (low, high) = Friendship.Order(me, target.Id);
            f = new Friendship { UserLowId = low, UserHighId = high, RequesterId = me, Status = FriendStatus.Pending };
            _db.Friendships.Add(f);
            try { await _db.SaveChangesAsync(); } // first, so the notification can point at the new id
            catch (DbUpdateException) // both of us sent at once: the unique pair index let only one row in
            {
                return Conflict(new { message = "A request between you two already exists." });
            }
            await SocialRules.Notify(_db, NotificationType.FriendRequest, target.Id, me, f.Id);
        }

        await _db.SaveChangesAsync();
        return Ok(new { state = f.Status == FriendStatus.Accepted ? "Friends" : "OutgoingPending", requestId = f.Id });
    }

    [HttpPost("friends/requests/{id:int}/accept")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Accept(int id)
    {
        var me = UserId;
        var f = await Incoming(id, me);
        if (f == null) return NotFound();
        await AcceptRow(f, me);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // declining is silent: the row and its notification go, nobody is told
    [HttpPost("friends/requests/{id:int}/decline")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Decline(int id)
    {
        var f = await Incoming(id, UserId);
        if (f == null) return NotFound();
        await SocialRules.Remove(_db, f);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // the sender takes a request back
    [HttpDelete("friends/requests/{id:int}")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Cancel(int id)
    {
        var me = UserId;
        var f = await _db.Friendships.FirstOrDefaultAsync(x => x.Id == id && x.RequesterId == me && x.Status == FriendStatus.Pending);
        if (f == null) return NotFound();
        await SocialRules.Remove(_db, f);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("friends/{username}")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Unfriend(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindVisible(_db, me, username);
        var f = other == null ? null : await SocialRules.FindFriendship(_db, me, other.Id);
        if (f == null || f.Status != FriendStatus.Accepted) return NotFound();
        await SocialRules.Remove(_db, f);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // silent: the blocked user is told nothing; their requests and my profile just 404 for them
    [HttpPut("blocks/{username}")]
    [EnableRateLimiting("block")]
    public async Task<IActionResult> Block(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindVisible(_db, me, username);
        if (other == null) return NotFound(new { message = "No user with that username." });
        if (other.Id == me) return BadRequest(new { message = "You can't block yourself." });

        var f = await SocialRules.FindFriendship(_db, me, other.Id);
        if (f != null) await SocialRules.Remove(_db, f);
        if (!await SocialRules.HasBlocked(_db, me, other.Id)) _db.UserBlocks.Add(new UserBlock { BlockerId = me, BlockedId = other.Id });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { } // a second tab blocked at the same moment: already done
        return NoContent();
    }

    [HttpDelete("blocks/{username}")]
    [EnableRateLimiting("block")]
    public async Task<IActionResult> Unblock(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindByName(_db, username);
        var block = other == null ? null : await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == other.Id);
        if (block == null) return NotFound();
        _db.UserBlocks.Remove(block);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // a pending request that is addressed to me (not one I sent); anything else is a 404 so ids can't be probed
    Task<Friendship?> Incoming(int id, string me) =>
        _db.Friendships.FirstOrDefaultAsync(f => f.Id == id && f.Status == FriendStatus.Pending && f.RequesterId != me
            && (f.UserLowId == me || f.UserHighId == me));

    // marks the row accepted, reads my copy of the request and tells the sender (unless they turned that off); the caller saves
    async Task AcceptRow(Friendship f, string me)
    {
        f.Status = FriendStatus.Accepted;
        f.RespondedAt = DateTime.UtcNow;
        foreach (var n in await _db.Notifications.Where(n => n.UserId == me && n.SubjectId == f.Id && n.Type == NotificationType.FriendRequest && n.ReadAt == null).ToListAsync())
            n.ReadAt = DateTime.UtcNow;
        await SocialRules.Notify(_db, NotificationType.FriendAccepted, f.RequesterId, me, f.Id);
    }
}
