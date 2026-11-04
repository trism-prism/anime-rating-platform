using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

// the signed-in devices of the caller; every query is scoped to the user in the token
[ApiController]
[Authorize]
[Route("me/sessions")]
public class SessionsController : ControllerBase
{
    readonly SessionService _sessions;

    public SessionsController(SessionService sessions) => _sessions = sessions;

    string UserId => User.FindFirst("sub")!.Value;
    string? CurrentId => User.FindFirst("sid")?.Value;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _sessions.Active(UserId);
        return Ok(list.Select(s => new
        {
            s.Id, s.CreatedAt, s.LastUsedAt, s.UserAgent, s.Ip,
            current = s.Id.ToString() == CurrentId
        }));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (!await _sessions.Revoke(UserId, id)) return NotFound();
        return NoContent();
    }

    [HttpPost("revoke-others")]
    public async Task<IActionResult> RevokeOthers()
    {
        await _sessions.RevokeAll(UserId, Guid.TryParse(CurrentId, out var sid) ? sid : null);
        return NoContent();
    }
}
