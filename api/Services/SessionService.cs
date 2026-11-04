using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace api.Services;

public record Refreshed(Session Session, AppUser User, string RefreshToken);

// sessions, rotating refresh tokens (cookie) and short access tokens (JWT)
public class SessionService
{
    static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    // the browser sees the API under /api (Vercel rewrite, Vite proxy), so the cookie path needs the prefix
    const string CookiePath = "/api/auth";

    readonly AppDbContext _db;
    readonly UserManager<AppUser> _users;
    readonly IConfiguration _config;
    readonly IHostEnvironment _env;

    public SessionService(AppDbContext db, UserManager<AppUser> users, IConfiguration config, IHostEnvironment env)
    {
        _db = db;
        _users = users;
        _config = config;
        _env = env;
    }

    public static string CookieName(string userId) => "rt_" + userId;

    // only the hash is stored, so a database leak gives no usable token
    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    static string Trim(string? s, int max) => s == null ? "" : s.Length > max ? s[..max] : s;
    static bool IsActive(Session s) => s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow;

    public async Task<(Session Session, string RefreshToken)> Create(AppUser user, string? userAgent, string? ip)
    {
        var now = DateTime.UtcNow;
        var token = NewToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + Lifetime,
            UserAgent = Trim(userAgent, 200),
            Ip = Trim(ip, 45),
            SecurityStamp = user.SecurityStamp ?? "",
            CurrentTokenHash = Hash(token)
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();
        return (session, token);
    }

    // returns null when the token is unknown, old, revoked or expired
    public async Task<Refreshed?> Refresh(string userId, string token)
    {
        var hash = Hash(token);
        var s = await _db.Sessions.FirstOrDefaultAsync(x => x.UserId == userId && (x.CurrentTokenHash == hash || x.PreviousTokenHash == hash));
        if (s == null || !IsActive(s)) return null;

        // an old token coming back means a copy was stolen: end the session. A changed stamp means the password was reset.
        var user = await _users.FindByIdAsync(userId);
        if (s.CurrentTokenHash != hash || user == null || user.SecurityStamp != s.SecurityStamp)
        {
            s.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return null;
        }

        var next = NewToken();
        s.PreviousTokenHash = s.CurrentTokenHash;
        s.CurrentTokenHash = Hash(next);
        s.LastUsedAt = DateTime.UtcNow;
        s.ExpiresAt = s.LastUsedAt + Lifetime;
        await _db.SaveChangesAsync();
        return new Refreshed(s, user, next);
    }

    // checked on every request, so "sign out this device" works at once
    public Task<bool> IsActive(string? sid)
    {
        if (!Guid.TryParse(sid, out var id)) return Task.FromResult(false);
        var now = DateTime.UtcNow;
        return _db.Sessions.AnyAsync(s => s.Id == id && s.RevokedAt == null && s.ExpiresAt > now);
    }

    public Task<List<Session>> Active(string userId)
    {
        var now = DateTime.UtcNow;
        return _db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt).ToListAsync();
    }

    // false when the session does not exist or belongs to someone else
    public async Task<bool> Revoke(string userId, Guid id)
    {
        var s = await _db.Sessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (s == null) return false;
        s.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task RevokeAll(string userId, Guid? except = null)
    {
        // ponytail: dead rows are never deleted; add a cleanup job if the table grows
        foreach (var s in await Active(userId))
            if (s.Id != except) s.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // logout: the cookie's token identifies the session
    public async Task RevokeByToken(string userId, string token)
    {
        var hash = Hash(token);
        var s = await _db.Sessions.FirstOrDefaultAsync(x => x.UserId == userId && (x.CurrentTokenHash == hash || x.PreviousTokenHash == hash));
        if (s == null) return;
        s.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<string> AccessToken(AppUser user, Guid sid)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Sid, sid.ToString())
        };
        claims.AddRange((await _users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    CookieOptions CookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = !_env.IsDevelopment(), // plain http only on a local dev machine
        SameSite = SameSiteMode.Lax,
        Path = CookiePath,
        MaxAge = maxAge
    };

    public void SetCookie(HttpResponse res, string userId, string token) => res.Cookies.Append(CookieName(userId), token, CookieOptions(Lifetime));
    // no Max-Age here: browsers prefer it over Expires, which would keep the cookie alive
    public void DeleteCookie(HttpResponse res, string userId) => res.Cookies.Delete(CookieName(userId), CookieOptions(null));
}
