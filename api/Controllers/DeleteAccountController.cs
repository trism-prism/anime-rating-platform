using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

public record DeleteAccountRequest(string? CurrentPassword, string? Code);

// Deleting your own account (it is anonymized, see AccountDeletion). Like export it needs the current password; an account that signs in with Google only has no
// password, so it asks for a fresh code emailed to the account address instead (the same pattern as verifying the email).
[ApiController]
[Authorize]
[EnableRateLimiting("account-delete")]
[Route("me/delete")]
public class DeleteAccountController : ControllerBase
{
    readonly AppDbContext _db;
    readonly UserManager<AppUser> _users;
    readonly EmailSender _email;
    readonly SessionService _sessions;
    readonly CredentialCheck _check;
    readonly IMemoryCache _cache;
    readonly ILogger<DeleteAccountController> _log;

    public DeleteAccountController(AppDbContext db, UserManager<AppUser> users, EmailSender email, SessionService sessions, CredentialCheck check, IMemoryCache cache, ILogger<DeleteAccountController> log)
    {
        _db = db;
        _users = users;
        _email = email;
        _sessions = sessions;
        _check = check;
        _cache = cache;
        _log = log;
    }

    string Me => User.FindFirst("sub")!.Value;

    // step one for accounts without a password: email a code, at most one per minute
    [HttpPost("code")]
    public async Task<IActionResult> SendCode()
    {
        var user = await _users.FindByIdAsync(Me);
        if (user == null) return Unauthorized();
        if (await _users.HasPasswordAsync(user)) return BadRequest(new { message = "Use your password to delete the account." });

        var key = "delete-code:" + user.Id;
        if (_cache.TryGetValue(key, out _)) return StatusCode(429, new { message = "A code was just sent. Wait a minute before asking again." });
        _cache.Set(key, true, TimeSpan.FromSeconds(60));
        try { await _email.SendDeleteCode(_users, user); }
        catch (Exception ex) // the exception can hold the code, so log only the user id
        {
            _log.LogError(ex.GetType().Name + " sending deletion code for user {UserId}", user.Id);
            _cache.Remove(key);
            return StatusCode(503, new { message = "Could not send the email right now. Try again later." });
        }
        return Ok(new { message = "Code sent. Check your email." });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(DeleteAccountRequest req)
    {
        var user = await _users.FindByIdAsync(Me);
        if (user == null) return Unauthorized();

        if (await _users.HasPasswordAsync(user))
        {
            var check = await _check.Password(user, req.CurrentPassword ?? "");
            if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");
        }
        else
        {
            // 400, not 401: the web client would retry a 401
            var key = "delete-tries:" + user.Id;
            if (!EmailCodes.Try(_cache, key)) return StatusCode(429, new { message = "Too many attempts, wait 15 minutes." });
            if (!await EmailCodes.Right(_users, user, EmailCodes.DeleteAccount, req.Code)) return BadRequest(new { message = "Wrong or expired code." });
            EmailCodes.Clear(_cache, key);
        }

        // without an admin nobody receives support messages or can moderate
        if (await _users.IsInRoleAsync(user, "Admin") && (await _users.GetUsersInRoleAsync("Admin")).Count <= 1)
            return Conflict(new { message = "You are the only admin. Make someone else an admin first." });

        var email = user.Email;
        if (!await AccountDeletion.Delete(_db, user.Id)) return Unauthorized();
        _sessions.DeleteCookie(Response, user.Id);

        if (!string.IsNullOrEmpty(email))
        {
            try { await _email.Send(email, "Your account was deleted", "Your AnimeRate account was deleted. Your ratings and reviews stay on the site as \"Deleted user\". If this was not you, contact support right away."); }
            catch (Exception ex) { _log.LogError(ex.GetType().Name + " sending the deletion notice for user {UserId}", user.Id); }
        }
        return NoContent();
    }
}
