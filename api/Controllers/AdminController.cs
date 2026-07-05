using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[Authorize]
[AdminOnly]
[Route("admin")]
public class AdminController : ControllerBase
{
    readonly UserManager<AppUser> _users;

    public AdminController(UserManager<AppUser> users) => _users = users;

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromServices] AniListImporter importer) => Ok(await importer.Import());

    [HttpGet("users")]
    public async Task<IActionResult> Users(string? search, int page = 1, int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);
        var q = _users.Users;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (s.Length > 100) s = s[..100];
            q = q.Where(u => u.UserName!.ToLower().Contains(s.ToLower()) || u.Email!.ToLower().Contains(s.ToLower()));
        }
        var total = await q.CountAsync();
        var rows = await q.OrderBy(u => u.UserName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.UserName, u.Email, u.EmailConfirmed }).ToListAsync();

        var admins = (await _users.GetUsersInRoleAsync("Admin")).Select(a => a.Id).ToHashSet();
        var items = rows.Select(u => new
        {
            id = u.Id, u.UserName, u.Email, emailVerified = u.EmailConfirmed, isAdmin = admins.Contains(u.Id)
        });
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("users/{id}/admin")]
    public async Task<IActionResult> Promote(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (!await _users.IsInRoleAsync(user, "Admin")) await _users.AddToRoleAsync(user, "Admin");
        return NoContent();
    }

    [HttpDelete("users/{id}/admin")]
    public async Task<IActionResult> Demote(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (!await _users.IsInRoleAsync(user, "Admin")) return NoContent();
        if ((await _users.GetUsersInRoleAsync("Admin")).Count <= 1)
            return BadRequest(new { message = "Cannot remove the last admin." });
        await _users.RemoveFromRoleAsync(user, "Admin");
        return NoContent();
    }
}
