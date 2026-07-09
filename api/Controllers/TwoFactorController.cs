using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

public record PasswordRequest(string CurrentPassword);

// two-step verification for the caller: when on, login asks for a code emailed to the account address
[ApiController]
[Authorize]
[Route("me/2fa")]
public class TwoFactorController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly SessionService _sessions;
    readonly CredentialCheck _check;

    public TwoFactorController(UserManager<AppUser> users, SessionService sessions, CredentialCheck check)
    {
        _users = users;
        _sessions = sessions;
        _check = check;
    }

    Task<AppUser?> Current() => _users.FindByIdAsync(User.FindFirst("sub")!.Value);
    Guid? CurrentSession => Guid.TryParse(User.FindFirst("sid")?.Value, out var sid) ? sid : null;

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        return Ok(new { enabled = user.TwoFactorEnabled });
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable(PasswordRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (user.TwoFactorEnabled) return BadRequest(new { message = "Two-step verification is already on." });

        var check = await _check.Password(user, req.CurrentPassword);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");
        // the codes go to the account address, so it has to be one the owner proved
        if (!user.EmailConfirmed) return BadRequest(new { message = "Verify your email first. The sign-in codes are sent there." });

        await _users.SetTwoFactorEnabledAsync(user, true);
        await _sessions.KeepOnly(user.Id, CurrentSession);
        return NoContent();
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(PasswordRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (!user.TwoFactorEnabled) return BadRequest(new { message = "Two-step verification is not on." });

        var check = await _check.Password(user, req.CurrentPassword);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _sessions.KeepOnly(user.Id, CurrentSession);
        return NoContent();
    }
}
