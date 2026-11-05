using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ChangeEmailRequest(string NewEmail, string CurrentPassword);
public record ChangeUserNameRequest(string UserName);

[ApiController]
[Authorize]
[Route("me")]
public class AccountController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly EmailSender _email;
    readonly SessionService _sessions;
    readonly CredentialCheck _check;
    readonly IMemoryCache _cache;
    readonly ILogger<AccountController> _log;

    public AccountController(UserManager<AppUser> users, EmailSender email, SessionService sessions, CredentialCheck check, IMemoryCache cache, ILogger<AccountController> log)
    {
        _users = users;
        _email = email;
        _sessions = sessions;
        _check = check;
        _cache = cache;
        _log = log;
    }

    // MapInboundClaims is off, so the id is the raw "sub" claim
    Task<AppUser?> Current() => _users.FindByIdAsync(User.FindFirst("sub")!.Value);
    Guid? CurrentSession => Guid.TryParse(User.FindFirst("sid")?.Value, out var sid) ? sid : null;

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        var check = await _check.Password(user, req.CurrentPassword);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        var result = await _users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _sessions.KeepOnly(user.Id, CurrentSession);
        return Ok(new { message = "Password changed. Your other devices were signed out." });
    }

    // sends the confirm link to the new address and a notice to the old one; nothing changes until the link is used
    [HttpPost("email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        var check = await _check.Password(user, req.CurrentPassword);
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        var newEmail = (req.NewEmail ?? "").Trim();
        if (!new EmailAddressAttribute().IsValid(newEmail)) return BadRequest(new { message = "Enter a valid email address." });
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "That is already your email." });
        if (await _users.FindByEmailAsync(newEmail) != null) return BadRequest(new { message = "That email is already in use." });

        // the cooldown keeps this from being used to mail strangers
        var key = "change-email:" + user.Id;
        if (_cache.TryGetValue(key, out _))
            return StatusCode(429, new { message = "A confirmation email was just sent. Wait a minute before asking again." });
        _cache.Set(key, true, TimeSpan.FromSeconds(60));

        try { await _email.SendEmailChange(_users, user, newEmail); }
        catch (Exception ex) // the exception can hold the link, so log only the user id
        {
            _log.LogError(ex.GetType().Name + " sending email-change mail for user {UserId}", user.Id);
            _cache.Remove(key);
            return StatusCode(503, new { message = "Could not send the email right now. Try again later." });
        }
        try { await _email.Send(user.Email!, "Email change requested", $"Someone requested to change your email to {newEmail}. If it was not you, change your password now."); }
        catch (Exception ex) { _log.LogError(ex.GetType().Name + " sending email-change notice for user {UserId}", user.Id); }

        return Ok(new { message = $"Check {newEmail} for a confirmation link. Your email changes when you open it." });
    }

    // the display name shown on reviews; no password needed, it is not a login credential
    [HttpPut("username")]
    public async Task<IActionResult> ChangeUserName(ChangeUserNameRequest req)
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        var name = (req.UserName ?? "").Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z0-9._-]{3,30}$"))
            return BadRequest(new { message = "Use 3 to 30 letters, digits, dots, underscores or dashes." });
        if (name == user.UserName) return Ok(new { userName = name });

        // not SetUserNameAsync: it renews the security stamp, which would sign out every other device
        user.UserName = name;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(new { message = "That display name is not available." });
        return Ok(new { userName = name });
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        return Ok(new
        {
            user.Email, user.UserName, emailVerified = user.EmailConfirmed, hasPassword = await _users.HasPasswordAsync(user),
            isAdmin = await _users.IsInRoleAsync(user, "Admin")
        });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail()
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        if (user.EmailConfirmed) return Ok(new { message = "Already verified." });

        var key = "verify-email:" + user.Id;
        if (_cache.TryGetValue(key, out _))
            return StatusCode(429, new { message = "A verification email was just sent. Wait a minute before asking again." });
        _cache.Set(key, true, TimeSpan.FromSeconds(60));

        try { await _email.SendConfirmation(_users, user); }
        catch (Exception ex) // the exception can hold the link, so log only the user id
        {
            _log.LogError(ex.GetType().Name + " sending confirmation email for user {UserId}", user.Id);
            _cache.Remove(key);
            return StatusCode(503, new { message = "Could not send the email right now. Try again later." });
        }
        return Ok(new { message = "Verification email sent." });
    }
}
