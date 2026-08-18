using api.Data;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// The optional ?scope= on read routes. Site-wide routes (averages, rankings, review lists): All = every rating, as before,
// regardless of audience; Friends = ratings by the viewer's accepted friends that the viewer rule lets them see; Me = their own.
// Profile routes read it differently (see ByAudience). An anonymous viewer always gets All.
public enum RatingScope { All, Friends, Me }

// the resolved request: the mode in effect, the viewer, or the error result to return
public record ScopeCall(RatingScope Mode, string? Viewer, IActionResult? Error)
{
    public bool Scoped => Mode != RatingScope.All;
}

// plain properties, not a constructor, so Npgsql can translate GroupBy on it
public class TitleTotal { public int TitleId { get; set; } public int N { get; set; } public double Sum { get; set; } }

public static class RatingScopes
{
    // empty = All; anything else must be a name (any case), so "1" or "all,me" is a 400
    public static ScopeCall Resolve(ControllerBase c, string? scope)
    {
        var mode = RatingScope.All;
        if (!string.IsNullOrEmpty(scope) && !ReviewRules.TryName(scope, out mode))
            return new ScopeCall(mode, null, c.BadRequest("scope must be all, friends or me"));
        var viewer = c.User?.FindFirst("sub")?.Value; // MapInboundClaims is off: the raw "sub" claim
        // a stale token must get the 401 that makes the client refresh, not a silent anonymous answer
        if (mode != RatingScope.All && viewer == null && c.HttpContext != null && SocialRules.ExpiredToken(c.Request, c.User!))
            return new ScopeCall(mode, null, c.Unauthorized());
        return new ScopeCall(viewer == null ? RatingScope.All : mode, viewer, null);
    }

    // site-wide author filter. Friends reuses VisibleTo, so a friend's OnlyMe rating and blocked pairs drop out.
    public static IQueryable<Rating> InScope(this IQueryable<Rating> q, AppDbContext db, ScopeCall s)
    {
        if (!s.Scoped) return q;
        var me = s.Viewer!;
        if (s.Mode == RatingScope.Me) return q.Where(r => r.UserId == me);
        return q.Where(r => db.Friendships.Any(f => f.Status == FriendStatus.Accepted
                && ((f.UserLowId == r.UserId && f.UserHighId == me) || (f.UserLowId == me && f.UserHighId == r.UserId))))
            .VisibleTo(db, me, false);
    }

    // profile filter: by the rating's own Audience. Friends = shared with friends only; Me = kept to themselves (own profile only,
    // the caller rejects it for anyone else). The viewer rule still applies on top.
    public static IQueryable<Rating> ByAudience(this IQueryable<Rating> q, ScopeCall s) => s.Mode switch
    {
        RatingScope.Friends => q.Where(r => r.Audience == Audience.Friends),
        RatingScope.Me => q.Where(r => r.Audience == Audience.OnlyMe),
        _ => q
    };

    // whole-title count and sum of Overall per title, for the ratings given (the scoped stand-in for Title.RatingCount/RatingSum)
    public static async Task<Dictionary<int, (int N, double Sum)>> TitleTotals(this IQueryable<Rating> q) =>
        (await q.Where(r => r.Volume == null).GroupBy(r => r.TitleId)
            .Select(g => new TitleTotal { TitleId = g.Key, N = g.Count(), Sum = g.Sum(r => r.Overall) }).ToListAsync())
        .ToDictionary(t => t.TitleId, t => (t.N, t.Sum));
}
