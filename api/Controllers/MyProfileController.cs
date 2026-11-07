using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record BioRequest(string? Bio);

// Your own bio and profile picture. Everyone reads them through the profile routes.
[ApiController]
[Authorize]
[EnableRateLimiting("profile-edit")]
[Route("me")]
public class MyProfileController : ControllerBase
{
    readonly AppDbContext _db;

    public MyProfileController(AppDbContext db) => _db = db;

    string Me => User.FindFirst("sub")!.Value;

    // empty clears it; slurs are masked like reviews
    [HttpPut("bio")]
    public async Task<IActionResult> SetBio(BioRequest body)
    {
        var bio = body.Bio?.Trim();
        if (bio?.Length > Avatars.MaxBio) return BadRequest(new { message = $"The bio must be {Avatars.MaxBio} characters or fewer." });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == Me && u.DeletedAt == null);
        if (user == null) return NotFound();
        user.Bio = string.IsNullOrEmpty(bio) ? null : Censor.Mask(bio);
        await _db.SaveChangesAsync();
        return Ok(new { bio = user.Bio });
    }

    // the raw image bytes as the body (the web client sends a 256x256 square); the type comes from the bytes, not the header
    [HttpPut("avatar")]
    [RequestSizeLimit(Avatars.MaxBytes)]
    public async Task<IActionResult> SetAvatar()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        if (ms.Length > Avatars.MaxBytes) return BadRequest(new { message = "The picture must be 2 MB or smaller." });
        var data = ms.ToArray();
        var type = Avatars.Sniff(data);
        if (type == null) return BadRequest(new { message = "The picture must be a png, jpeg or webp image." });

        var me = Me;
        if (!await _db.Users.AnyAsync(u => u.Id == me && u.DeletedAt == null)) return NotFound();
        var row = await _db.Avatars.FirstOrDefaultAsync(a => a.UserId == me);
        if (row == null) _db.Avatars.Add(row = new UserAvatar { UserId = me });
        row.Data = data;
        row.ContentType = type;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> RemoveAvatar()
    {
        var me = Me;
        _db.Avatars.RemoveRange(await _db.Avatars.Where(a => a.UserId == me).ToListAsync());
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
