using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// records and lists security events (login, password change, 2FA, ...)
public class SecurityLog
{
    readonly AppDbContext _db;

    public SecurityLog(AppDbContext db) => _db = db;

    public async Task Add(string userId, string type, HttpContext http)
    {
        _db.SecurityEvents.Add(new SecurityEvent
        {
            UserId = userId,
            Type = type,
            Ip = SessionService.Trim(http.Connection.RemoteIpAddress?.ToString(), 45),
            UserAgent = SessionService.Trim(http.Request.Headers.UserAgent.ToString(), 200),
            At = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public Task<List<SecurityEvent>> Latest(string userId) =>
        _db.SecurityEvents.Where(e => e.UserId == userId).OrderByDescending(e => e.At).Take(50).ToListAsync();
}
