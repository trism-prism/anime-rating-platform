using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

public record EnableTwoFactorRequest(string CurrentPassword, string Code);
public record DisableTwoFactorRequest(string CurrentPassword, string Code);
public record PasswordRequest(string CurrentPassword);

// authenticator-app two-factor sign-in for the caller
[ApiController]
[Authorize]
[Route("me/2fa")]
public class TwoFactorController : ControllerBase
{
    const string Issuer = "Anime Rating Platform";

    readonly UserManager<AppUser> _users;
    readonly SessionService _sessions;
    readonly CredentialCheck _check;
    readonly SecurityLog _events;

    public TwoFactorController(UserManager<AppUser> users, SessionService sessions, CredentialCheck check, SecurityLog events)
    {
        _users = users;
        _sessions = sessions;
        _check = check;
        _events = events;
    }

    Task<AppUser?> Current() => _users.FindByIdAsync(User.FindFirst("sub")!.Value);
    Guid? CurrentSession => Guid.TryParse(User.FindFirst("sid")?.Value, out var sid) ? sid : null;

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        return Ok(new { enabled = user.TwoFactorEnabled, recoveryCodesLeft = await _users.CountRecoveryCodesAsync(user) });
    }

    // the key to type into the authenticator app, and the same as an otpauth link for the QR code.
    // The key is a secret, so it needs the password like the rest (POST: a GET has no body).
    [HttpPost("setup")]
    public async Task<IActionResult> Setup(PasswordRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (user.TwoFactorEnabled) return BadRequest(new { message = "Two-factor authentication is already on." });
        var check = await _check.Password(user, req.CurrentPassword, HttpContext);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);
        }
        var label = Uri.EscapeDataString(Issuer) + ":" + Uri.EscapeDataString(user.Email!);
        return Ok(new { key, uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6" });
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable(EnableTwoFactorRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (user.TwoFactorEnabled) return BadRequest(new { message = "Two-factor authentication is already on." });

        var check = await _check.Password(user, req.CurrentPassword, HttpContext);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");
        // app codes only: recovery codes left from an earlier setup must not turn it on
        check = await _check.Code(user, req.Code, HttpContext, allowRecovery: false);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Invalid code.");

        await _users.SetTwoFactorEnabledAsync(user, true);
        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await _sessions.KeepOnly(user.Id, CurrentSession);
        await _events.Add(user.Id, "2fa-enabled", HttpContext);
        return Ok(new { recoveryCodes = codes }); // shown once
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(DisableTwoFactorRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (!user.TwoFactorEnabled) return BadRequest(new { message = "Two-factor authentication is not on." });

        var check = await _check.Password(user, req.CurrentPassword, HttpContext);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");
        check = await _check.Code(user, req.Code, HttpContext);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Invalid code.");

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user); // a later setup gets a new key
        await _sessions.KeepOnly(user.Id, CurrentSession);
        await _events.Add(user.Id, "2fa-disabled", HttpContext);
        return NoContent();
    }

    [HttpPost("recovery-codes")]
    public async Task<IActionResult> NewRecoveryCodes(PasswordRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (!user.TwoFactorEnabled) return BadRequest(new { message = "Two-factor authentication is not on." });

        var check = await _check.Password(user, req.CurrentPassword, HttpContext);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await _events.Add(user.Id, "recovery-codes-regenerated", HttpContext);
        return Ok(new { recoveryCodes = codes });
    }
}
