using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

[ApiController]
[Authorize]
[Route("me")]
public class AccountController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly EmailSender _email;
    readonly IMemoryCache _cache;
    readonly ILogger<AccountController> _log;

    public AccountController(UserManager<AppUser> users, EmailSender email, IMemoryCache cache, ILogger<AccountController> log)
    {
        _users = users;
        _email = email;
        _cache = cache;
        _log = log;
    }

    // MapInboundClaims is off, so the id is the raw "sub" claim
    Task<AppUser?> Current() => _users.FindByIdAsync(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await Current();
        if (user == null) return Unauthorized();
        return Ok(new
        {
            user.Email, user.UserName, emailVerified = user.EmailConfirmed,
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
