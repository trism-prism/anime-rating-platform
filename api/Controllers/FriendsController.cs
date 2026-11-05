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
    public const int MaxPendingIncoming = 100; // per recipient, so nobody's inbox can be flooded
    const int MaxFriendsListed = 500, MaxRequestsListed = 100;
    const string NoUser = "No user with that username.";

    readonly AppDbContext _db;

    public FriendsController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the plain "sub" claim; ids never come from the body or the route
    string UserId => User.FindFirst("sub")!.Value;

    // accepted friends plus pending requests in both directions (each list is capped)
    [HttpGet("me/friends")]
    public async Task<IActionResult> List()
    {
        var me = UserId;
        var mine = _db.Friendships.Where(f => f.UserLowId == me || f.UserHighId == me);
        var friends = await mine.Where(f => f.Status == FriendStatus.Accepted).OrderByDescending(f => f.Id).Take(MaxFriendsListed).ToListAsync();
        var incoming = await mine.Where(f => f.Status == FriendStatus.Pending && f.RequesterId != me).OrderByDescending(f => f.Id).Take(MaxRequestsListed).ToListAsync();
        var outgoing = await mine.Where(f => f.Status == FriendStatus.Pending && f.RequesterId == me).OrderByDescending(f => f.Id).Take(MaxRequestsListed).ToListAsync();

        var ids = friends.Concat(incoming).Concat(outgoing).Select(f => f.OtherId(me)).Distinct().ToList();
        var names = await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName!);

        return Ok(new
        {
            friends = friends.Select(f => new { userName = names[f.OtherId(me)], since = f.RespondedAt ?? f.CreatedAt }),
            incoming = incoming.Select(f => new { requestId = f.Id, userName = names[f.RequesterId], sentAt = f.CreatedAt }),
            outgoing = outgoing.Select(f => new { requestId = f.Id, userName = names[f.OtherId(me)], sentAt = f.CreatedAt })
        });
    }

    // by exact username. If they already asked me, this accepts. Someone who blocked me gets the same answer as an unknown name.
    [HttpPost("friends/requests")]
    [EnableRateLimiting("friend-request")]
    public async Task<IActionResult> Send(SendFriendRequest req)
    {
        var me = UserId;
        var target = await SocialRules.FindVisible(_db, me, req.Username);
        if (target == null) return NotFound(new { message = NoUser });
        var targetId = target.Id;
        if (targetId == me) return BadRequest(new { message = "You can't add yourself." });
        if (await SocialRules.HasBlocked(_db, me, targetId)) return Conflict(new { message = "You blocked this user. Unblock them first." });

        var f = await SocialRules.FindFriendship(_db, me, targetId);
        var created = false;
        if (f == null)
        {
            if (await PendingFor(targetId) >= MaxPendingIncoming)
                return Conflict(new { message = "This user has too many pending requests. Try again later." });

            var (low, high) = Friendship.Order(me, targetId);
            try
            {
                f = await CreateRequest(new Friendship { UserLowId = low, UserHighId = high, RequesterId = me, Status = FriendStatus.Pending }, targetId, me);
                created = true;
            }
            catch (DbUpdateException)
            {
                // Either the other user asked me at the same moment (the unique pair index let only one row in), or
                // something else failed (a user was deleted). Look again instead of guessing from the exception.
                _db.ChangeTracker.Clear();
                f = await SocialRules.FindFriendship(_db, me, targetId);
                if (f == null) return NotFound(new { message = NoUser });
            }
        }

        if (!created)
        {
            if (f.Status == FriendStatus.Accepted) return Conflict(new { message = "You are already friends." });
            if (f.RequesterId == me) return Conflict(new { message = "You already sent this user a request." });
            if (!await AcceptAndSave(f, me)) return Conflict(new { message = "That request just changed. Try again." });
        }
        else
        {
            // a block may have landed between the checks above and the insert (Block deletes after it inserts, so one side always sees the other)
            var theyBlocked = await SocialRules.HasBlocked(_db, targetId, me);
            if (theyBlocked || await SocialRules.HasBlocked(_db, me, targetId))
            {
                await SocialRules.Remove(_db, f);
                await SocialRules.TrySave(_db);
                return theyBlocked ? NotFound(new { message = NoUser }) : Conflict(new { message = "You blocked this user. Unblock them first." });
            }
        }
        return Ok(new { state = f.Status == FriendStatus.Accepted ? "Friends" : "OutgoingPending", requestId = f.Id });
    }

    [HttpPost("friends/requests/{id:int}/accept")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Accept(int id)
    {
        var me = UserId;
        var f = await Incoming(id, me);
        // 404 also when someone answered or removed it a moment ago
        return f != null && await AcceptAndSave(f, me) ? NoContent() : NotFound();
    }

    // declining is silent: the row and its notification go, nobody is told
    [HttpPost("friends/requests/{id:int}/decline")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Decline(int id)
    {
        var f = await Incoming(id, UserId);
        if (f == null) return NotFound();
        await SocialRules.Remove(_db, f);
        // Status is a concurrency token: if the request was accepted meanwhile the delete matches nothing and the friendship stays
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
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
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound();
    }

    // Same 404 as an unknown name when that user blocked me. Nothing to undo (or already undone) is a 204.
    [HttpDelete("friends/{username}")]
    [EnableRateLimiting("notify")]
    public async Task<IActionResult> Unfriend(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindVisible(_db, me, username);
        if (other == null) return NotFound(new { message = NoUser });
        var f = await SocialRules.FindFriendship(_db, me, other.Id);
        if (f != null && f.Status == FriendStatus.Accepted)
        {
            await SocialRules.Remove(_db, f);
            await SocialRules.TrySave(_db); // false: someone removed it first, which is what was asked
        }
        return NoContent();
    }

    // Silent and idempotent: the blocked user is told nothing; their requests and my profile just 404 for them.
    // Works on anyone who exists, even someone who blocked me, and answers the same either way.
    [HttpPut("blocks/{username}")]
    [EnableRateLimiting("block")]
    public async Task<IActionResult> Block(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindByName(_db, username);
        if (other == null) return NotFound(new { message = NoUser });
        if (other.Id == me) return BadRequest(new { message = "You can't block yourself." });

        // the block goes in first and alone, so no failure further down can lose it
        if (!await SocialRules.HasBlocked(_db, me, other.Id))
        {
            _db.UserBlocks.Add(new UserBlock { BlockerId = me, BlockedId = other.Id });
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                if (!await SocialRules.HasBlocked(_db, me, other.Id)) return NotFound(new { message = NoUser }); // the user is gone
            }
        }

        // then end any friendship or request; a concurrent accept or unfriend can change the row under the delete, so look again
        for (var i = 0; i < 3; i++)
        {
            var f = await SocialRules.FindFriendship(_db, me, other.Id);
            if (f == null) break;
            await SocialRules.Remove(_db, f);
            if (await SocialRules.TrySave(_db)) break;
        }
        return NoContent();
    }

    [HttpDelete("blocks/{username}")]
    [EnableRateLimiting("block")]
    public async Task<IActionResult> Unblock(string username)
    {
        var me = UserId;
        var other = await SocialRules.FindByName(_db, username);
        if (other == null) return NotFound(new { message = NoUser });
        var block = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == other.Id);
        if (block != null)
        {
            _db.UserBlocks.Remove(block);
            await _db.SaveChangesAsync();
        }
        return NoContent(); // same answer whether or not there was a block
    }

    Task<int> PendingFor(string userId) =>
        _db.Friendships.CountAsync(f => f.Status == FriendStatus.Pending && f.RequesterId != userId && (f.UserLowId == userId || f.UserHighId == userId));

    // a pending request that is addressed to me (not one I sent) and has no block either way; anything else is a 404 so ids can't be probed
    Task<Friendship?> Incoming(int id, string me) =>
        _db.Friendships.FirstOrDefaultAsync(f => f.Id == id && f.Status == FriendStatus.Pending && f.RequesterId != me
            && (f.UserLowId == me || f.UserHighId == me)
            && !_db.UserBlocks.Any(b => (b.BlockerId == me && b.BlockedId == f.RequesterId) || (b.BlockerId == f.RequesterId && b.BlockedId == me)));

    // the pending row and the recipient's notification are saved together or not at all
    async Task<Friendship> CreateRequest(Friendship f, string recipientId, string me)
    {
        var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null; // InMemory (tests) has none
        await using (tx)
        {
            _db.Friendships.Add(f);
            await _db.SaveChangesAsync(); // first, so the notification can point at the new id
            await SocialRules.Notify(_db, NotificationType.FriendRequest, recipientId, me, f.Id);
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }
        return f;
    }

    // Accepts and saves; false when the row changed first (answered, cancelled, unfriended or blocked elsewhere), in which case
    // nothing is saved and nobody is notified. Status is a concurrency token, so two accepts can't both win.
    async Task<bool> AcceptAndSave(Friendship f, string me)
    {
        f.Status = FriendStatus.Accepted;
        f.RespondedAt = DateTime.UtcNow;
        foreach (var n in await _db.Notifications.Where(n => n.UserId == me && n.SubjectId == f.Id && n.Type == NotificationType.FriendRequest && n.ReadAt == null).ToListAsync())
            n.ReadAt = DateTime.UtcNow;
        await SocialRules.Notify(_db, NotificationType.FriendAccepted, f.RequesterId, me, f.Id);
        return await SocialRules.TrySave(_db);
    }
}
