using System.Text;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record ReplyRequest(string? Body, bool Spoiler);
public record ReactionRequest(string? Kind);

// Reviews (ratings with text), their replies and reactions. Every route applies the viewer rule from ReviewRules and answers a
// review the viewer may not see with the same empty 404 as one that does not exist.
[ApiController]
public class ReviewsController : ControllerBase
{
    const int Retries = 4;
    readonly AppDbContext _db;

    public ReviewsController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the raw "sub" claim; never read it from the request
    string? ViewerId => User.FindFirst("sub")?.Value;

    // ---- reading ----

    // whole-title and volume reviews of one title; sort=helpful (default) or newest; keyset paging, 10 per page
    [AllowAnonymous]
    [HttpGet("titles/{id:int}/reviews")]
    public async Task<IActionResult> ForTitle(int id, string? sort, string? cursor)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        if (!await _db.Titles.AnyAsync(t => t.Id == id)) return NotFound();
        return await Page(_db.Ratings.Where(r => r.TitleId == id), sort, cursor);
    }

    // whole-title reviews of the series' titles, each labelled with its title
    // ponytail: no index spans a series' titles; fine while a series has a handful of titles
    [AllowAnonymous]
    [HttpGet("series/{id:int}/reviews")]
    public async Task<IActionResult> ForSeries(int id, string? sort, string? cursor)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        if (!await _db.Series.AnyAsync(s => s.Id == id)) return NotFound();
        return await Page(_db.Ratings.Where(r => r.Volume == null && r.Title.SeriesId == id), sort, cursor);
    }

    [AllowAnonymous]
    [HttpGet("reviews/{id:int}")]
    public async Task<IActionResult> One(int id)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = ViewerId;
        var admin = await ReviewRules.IsAdmin(_db, viewer);
        var items = await Shape(_db.Ratings.Where(r => r.Id == id).Reviews().VisibleTo(_db, viewer, admin), viewer, admin);
        return items.Count == 0 ? NotFound() : Ok(items[0].Json);
    }

    // oldest first, keyset by id
    [AllowAnonymous]
    [HttpGet("reviews/{id:int}/replies")]
    public async Task<IActionResult> Replies(int id, int? cursor)
    {
        if (SocialRules.ExpiredToken(Request, User)) return Unauthorized();
        var viewer = ViewerId;
        var admin = await ReviewRules.IsAdmin(_db, viewer);
        var review = await ReviewRules.FindReview(_db, id, viewer, admin);
        if (review == null) return NotFound();

        var q = _db.Replies.Where(x => x.RatingId == id).VisibleTo(_db, viewer, admin);
        if (cursor != null) q = q.Where(x => x.Id > cursor);
        var rows = await q.OrderBy(x => x.Id).Take(ReviewRules.PageSize + 1)
            .Select(x => new { x.Id, UserName = x.User.UserName!, x.UserId, x.Body, x.Spoiler, x.CreatedAt }).ToListAsync();
        var more = rows.Count > ReviewRules.PageSize;
        rows = rows.Take(ReviewRules.PageSize).ToList();
        return Ok(new
        {
            items = rows.Select(x => ReplyJson(x.Id, x.UserName, x.UserId, x.Body, x.Spoiler, x.CreatedAt, viewer, review.UserId, admin)),
            nextCursor = more ? rows[^1].Id : (int?)null
        });
    }

    // ---- replies ----

    [Authorize]
    [EnableRateLimiting("reply")]
    [HttpPost("reviews/{id:int}/replies")]
    public async Task<IActionResult> PostReply(int id, ReplyRequest body)
    {
        var text = body.Body?.Trim() ?? "";
        if (text.Length < 1 || text.Length > ReviewRules.MaxReply) return BadRequest(new { message = $"A reply must be 1 to {ReviewRules.MaxReply} characters." });
        var me = ViewerId!;
        if (!await _db.Users.AnyAsync(u => u.Id == me && u.EmailConfirmed))
            return StatusCode(403, new { message = "Verify your email to reply." });
        var admin = await ReviewRules.IsAdmin(_db, me);
        var review = await ReviewRules.FindReview(_db, id, me, admin);
        if (review == null) return NotFound();

        var reply = new Reply { RatingId = id, UserId = me, Body = text, Spoiler = body.Spoiler };
        // the reply and the author's notification are saved together or not at all
        try
        {
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null; // InMemory (tests) has none
            _db.Replies.Add(reply);
            await _db.SaveChangesAsync(); // first, so the notification can point at the new id
            // an admin can see a review across a block, so the block is checked here too
            if (review.UserId != me && !await SocialRules.BlockedEither(_db, me, review.UserId))
            {
                await SocialRules.Notify(_db, NotificationType.Reply, review.UserId, me, reply.Id);
                await _db.SaveChangesAsync();
            }
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateException) // the review was deleted between the check and the insert
        {
            _db.ChangeTracker.Clear();
            return NotFound();
        }

        var name = await _db.Users.Where(u => u.Id == me).Select(u => u.UserName!).FirstAsync();
        return StatusCode(201, ReplyJson(reply.Id, name, me, reply.Body, reply.Spoiler, reply.CreatedAt, me, review.UserId, admin));
    }

    // the reply's author or the review's author; anyone else (and admins, who use /admin/replies) gets a plain 404
    [Authorize]
    [EnableRateLimiting("notify")]
    [HttpDelete("replies/{id:int}")]
    public async Task<IActionResult> DeleteReply(int id)
    {
        var me = ViewerId!;
        var row = await _db.Replies.Where(x => x.Id == id).Select(x => new { Reply = x, ReviewAuthor = x.Rating.UserId }).FirstOrDefaultAsync();
        if (row == null) return NotFound();
        var allowed = row.Reply.UserId == me || (row.ReviewAuthor == me && !await SocialRules.BlockedEither(_db, me, row.Reply.UserId));
        if (!allowed) return NotFound();

        _db.Replies.Remove(row.Reply);
        await SocialRules.RemoveReplyNotifications(_db, new[] { id });
        return await SocialRules.TrySave(_db) ? NoContent() : NotFound(); // false: someone deleted it first
    }

    // ---- reactions ----

    [Authorize]
    [EnableRateLimiting("reaction")]
    [HttpPut("reviews/{id:int}/reaction")]
    public async Task<IActionResult> React(int id, ReactionRequest body)
    {
        if (!ReviewRules.TryName<ReactionKind>(body.Kind, out var kind))
            return BadRequest(new { message = "Reaction must be Helpful, Unhelpful or Funny." });
        return await SetReaction(id, kind);
    }

    [Authorize]
    [EnableRateLimiting("reaction")]
    [HttpDelete("reviews/{id:int}/reaction")]
    public Task<IActionResult> Unreact(int id) => SetReaction(id, null);

    async Task<IActionResult> SetReaction(int id, ReactionKind? to)
    {
        var me = ViewerId!;
        var admin = await ReviewRules.IsAdmin(_db, me);
        for (var attempt = 0; attempt <= Retries; attempt++)
        {
            var review = await ReviewRules.FindReview(_db, id, me, admin);
            if (review == null) return NotFound();
            if (review.UserId == me) return BadRequest(new { message = "You can't react to your own review." });
            var existing = await _db.Reactions.FirstOrDefaultAsync(x => x.RatingId == id && x.UserId == me);
            if ((existing?.Kind) == to) return Ok(await ReactionState(id, me)); // nothing to change
            try
            {
                await Apply(review, existing, to, me);
                return Ok(await ReactionState(id, me));
            }
            catch (DbUpdateException) // a concurrent change by this user (token) or a double first click (unique index): look again
            {
                _db.ChangeTracker.Clear();
            }
        }
        return Conflict(new { message = "Try again." });
    }

    // The reaction row and the counters change in one transaction. The row is saved first and guarded (unique pair, Kind token),
    // so the counters move only for a change that really happened; they move by atomic increments, never read-then-write.
    async Task Apply(Rating review, Reaction? existing, ReactionKind? to, string me)
    {
        await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
        int h = 0, u = 0, f = 0;
        void Bump(ReactionKind k, int d)
        {
            if (k == ReactionKind.Helpful) h += d;
            else if (k == ReactionKind.Unhelpful) u += d;
            else f += d;
        }
        if (existing != null) Bump(existing.Kind, -1);
        if (to != null) Bump(to.Value, 1);

        if (existing == null)
        {
            _db.Reactions.Add(new Reaction { RatingId = review.Id, UserId = me, Kind = to!.Value });
            // told once, the first time this user reacts to this review (the notification index also allows one per reactor)
            if (!await SocialRules.BlockedEither(_db, me, review.UserId))
                await SocialRules.Notify(_db, NotificationType.Reaction, review.UserId, me, review.Id);
        }
        else if (to == null)
        {
            _db.Reactions.Remove(existing);
            _db.Notifications.RemoveRange(await _db.Notifications
                .Where(n => n.Type == NotificationType.Reaction && n.ActorId == me && n.SubjectId == review.Id).ToListAsync());
        }
        else existing.Kind = to.Value;
        await _db.SaveChangesAsync();

        if (_db.Database.IsNpgsql())
            await _db.Ratings.Where(r => r.Id == review.Id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.HelpfulCount, r => r.HelpfulCount + h)
                .SetProperty(r => r.UnhelpfulCount, r => r.UnhelpfulCount + u)
                .SetProperty(r => r.FunnyCount, r => r.FunnyCount + f)
                .SetProperty(r => r.Score, r => r.Score + h - u));
        else // InMemory (the tests) has no ExecuteUpdate; its tests are single-threaded
        {
            var r = await _db.Ratings.FirstAsync(x => x.Id == review.Id);
            r.HelpfulCount += h; r.UnhelpfulCount += u; r.FunnyCount += f; r.Score += h - u;
            await _db.SaveChangesAsync();
        }
        if (tx != null) await tx.CommitAsync();
    }

    async Task<object> ReactionState(int id, string me)
    {
        var c = await _db.Ratings.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.HelpfulCount, r.UnhelpfulCount, r.FunnyCount }).FirstAsync();
        var mine = await _db.Reactions.AsNoTracking().Where(x => x.RatingId == id && x.UserId == me).Select(x => (ReactionKind?)x.Kind).FirstOrDefaultAsync();
        return new { myReaction = mine?.ToString(), helpful = c.HelpfulCount, unhelpful = c.UnhelpfulCount, funny = c.FunnyCount };
    }

    // ---- shaping ----

    static object ReplyJson(int id, string userName, string authorId, string body, bool spoiler, DateTime createdAt, string? viewer, string reviewAuthorId, bool admin) => new
    {
        id, userName, body, spoiler, createdAt,
        mine = viewer != null && authorId == viewer,
        // matches DeleteReply (reply author, review author) plus the admin route
        canDelete = viewer != null && (authorId == viewer || reviewAuthorId == viewer || admin)
    };

    async Task<IActionResult> Page(IQueryable<Rating> source, string? sort, string? cursor)
    {
        var helpful = sort != "newest"; // anything else (or nothing) sorts by helpful
        var viewer = ViewerId;
        var admin = await ReviewRules.IsAdmin(_db, viewer);
        var q = source.Reviews().VisibleTo(_db, viewer, admin);
        if (cursor != null)
        {
            if (!TryDecode(cursor, helpful, out var score, out var last)) return BadRequest(new { message = "Bad cursor." });
            q = helpful ? q.Where(r => r.Score < score || (r.Score == score && r.Id < last)) : q.Where(r => r.Id < last);
        }
        q = helpful ? q.OrderByDescending(r => r.Score).ThenByDescending(r => r.Id) : q.OrderByDescending(r => r.Id);

        var items = await Shape(q.Take(ReviewRules.PageSize + 1), viewer, admin);
        var more = items.Count > ReviewRules.PageSize;
        items = items.Take(ReviewRules.PageSize).ToList();
        var lastItem = items.LastOrDefault();
        return Ok(new { items = items.Select(i => i.Json), nextCursor = more && lastItem != null ? Encode(helpful, lastItem.Score, lastItem.Id) : null });
    }

    class ReviewRow
    {
        public int Id { get; set; }
        public int TitleId { get; set; }
        public string TitleName { get; set; } = "";
        public int SeriesId { get; set; }
        public int? Volume { get; set; }
        public string UserName { get; set; } = "";
        public bool Mine { get; set; }
        public double Overall { get; set; }
        public int Story { get; set; }
        public int Art { get; set; }
        public int Characters { get; set; }
        public int Enjoyment { get; set; }
        public string ReviewText { get; set; } = "";
        public bool Spoiler { get; set; }
        public DateTime CreatedAt { get; set; }
        public int HelpfulCount { get; set; }
        public int UnhelpfulCount { get; set; }
        public int FunnyCount { get; set; }
        public int Score { get; set; }
        public object Json { get; set; } = null!;
    }

    // keeps the query's order; reply counts and the viewer's own reactions come from two extra queries for the page
    async Task<List<ReviewRow>> Shape(IQueryable<Rating> q, string? viewer, bool admin)
    {
        var rows = await q.Select(r => new ReviewRow
        {
            Id = r.Id, TitleId = r.TitleId, TitleName = r.Title.Name, SeriesId = r.Title.SeriesId, Volume = r.Volume,
            UserName = r.User.UserName!, Mine = r.UserId == viewer,
            Overall = r.Overall, Story = r.Story, Art = r.Art, Characters = r.Characters, Enjoyment = r.Enjoyment,
            ReviewText = r.ReviewText!, Spoiler = r.Spoiler, CreatedAt = r.CreatedAt,
            HelpfulCount = r.HelpfulCount, UnhelpfulCount = r.UnhelpfulCount, FunnyCount = r.FunnyCount, Score = r.Score
        }).ToListAsync();
        if (rows.Count == 0) return rows;

        var ids = rows.Select(r => r.Id).ToList();
        var counts = (await _db.Replies.Where(x => ids.Contains(x.RatingId)).VisibleTo(_db, viewer, admin)
            .GroupBy(x => x.RatingId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync()).ToDictionary(x => x.Id, x => x.N);
        var mine = viewer == null ? new Dictionary<int, ReactionKind>()
            : await _db.Reactions.Where(x => ids.Contains(x.RatingId) && x.UserId == viewer).ToDictionaryAsync(x => x.RatingId, x => x.Kind);

        foreach (var r in rows)
            r.Json = new
            {
                id = r.Id, r.TitleId, r.TitleName, r.SeriesId, r.Volume, r.UserName, mine = r.Mine,
                r.Overall, r.Story, r.Art, r.Characters, r.Enjoyment, r.ReviewText, r.Spoiler, r.CreatedAt,
                helpful = r.HelpfulCount, unhelpful = r.UnhelpfulCount, funny = r.FunnyCount,
                myReaction = mine.TryGetValue(r.Id, out var k) ? k.ToString() : null,
                replyCount = counts.GetValueOrDefault(r.Id)
            };
        return rows;
    }

    // the cursor is the last row's sort key: "score:id" for helpful, "id" for newest
    static string Encode(bool helpful, int score, int id) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(helpful ? $"{score}:{id}" : id.ToString()));

    static bool TryDecode(string cursor, bool helpful, out int score, out int id)
    {
        score = id = 0;
        try
        {
            var parts = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)).Split(':');
            return helpful
                ? parts.Length == 2 && int.TryParse(parts[0], out score) && int.TryParse(parts[1], out id)
                : parts.Length == 1 && int.TryParse(parts[0], out id);
        }
        catch (FormatException) { return false; }
    }
}
