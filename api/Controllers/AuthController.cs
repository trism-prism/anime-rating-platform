using System.Text;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace api.Controllers;

public record RegisterRequest(string Email, string Password, string? UserName);
public record EmailRequest(string Email);
public record LoginRequest(string Email, string Password);
public record UserIdRequest(string UserId);
public record ResetPasswordRequest(string UserId, string Token, string NewPassword);

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("auth")]
public class AuthController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly SignInManager<AppUser> _signIn;
    readonly EmailSender _email;
    readonly SessionService _sessions;
    readonly IConfiguration _config;
    readonly ILogger<AuthController> _log;

    public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn, EmailSender email, SessionService sessions, IConfiguration config, ILogger<AuthController> log)
    {
        _log = log;
        _users = users;
        _signIn = signIn;
        _email = email;
        _sessions = sessions;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        // default the public name to the email's local part so reviews don't show the email
        var name = req.UserName;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('@')) name = await UserNames.FromEmail(_users, req.Email);
        var user = new AppUser { Email = req.Email, UserName = name };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        if (!await TrySendConfirmation(user))
            return Ok(new { message = "Account created, but the confirmation email could not be sent. Verify your email in Settings later." });
        return Ok(new { message = "Check your email to confirm your account." });
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null) return BadRequest(new { message = "Invalid confirmation link." });

        string raw;
        try { raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return BadRequest(new { message = "Invalid confirmation link." }); }

        var result = await _users.ConfirmEmailAsync(user, raw);
        if (!result.Succeeded) return BadRequest(new { message = "Invalid or expired confirmation link." });
        await AdminBootstrap.TryPromote(_users, _config, user);
        return Ok(new { message = "Email confirmed. You can log in now." });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> Resend(EmailRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user != null && !user.EmailConfirmed && !await TrySendConfirmation(user))
            return StatusCode(503, new { message = "Could not send the email right now. Try again later." });
        // same answer either way
        return Ok(new { message = "If the account exists, a new confirmation email was sent." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user == null) return Unauthorized(new { message = "Invalid email or password." });

        var result = await _signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut) return StatusCode(423, new { message = "Too many failed attempts. Try again in 5 minutes." });
        if (!result.Succeeded)
        {
            // warn only when the lockout is close
            var left = _users.Options.Lockout.MaxFailedAccessAttempts - user.AccessFailedCount;
            if (left <= 3) return Unauthorized(new { message = $"Invalid email or password. {left} tries left." });
            return Unauthorized(new { message = "Invalid email or password." });
        }

        await AdminBootstrap.TryPromote(_users, _config, user);
        var (session, refreshToken) = await _sessions.Create(user, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        _sessions.SetCookie(Response, user.Id, refreshToken);
        return Ok(await SignedIn(user, session.Id));
    }

    // the browser picks the account by sending its id; the matching cookie holds that account's refresh token
    [DisableRateLimiting] // a page load refreshes once per account; the token is 256 random bits, nothing to guess
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(UserIdRequest req)
    {
        var token = Request.Cookies[SessionService.CookieName(req.UserId)];
        var r = token == null ? null : await _sessions.Refresh(req.UserId, token);
        if (r == null)
        {
            _sessions.DeleteCookie(Response, req.UserId);
            return Unauthorized(new { message = "Session ended. Log in again." });
        }
        _sessions.SetCookie(Response, req.UserId, r.RefreshToken);
        return Ok(await SignedIn(r.User, r.Session.Id));
    }

    [DisableRateLimiting]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(UserIdRequest req)
    {
        var token = Request.Cookies[SessionService.CookieName(req.UserId)];
        if (token != null) await _sessions.RevokeByToken(req.UserId, token);
        _sessions.DeleteCookie(Response, req.UserId);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user != null) await TrySendPasswordReset(user);
        // same answer whether or not the email exists, or the send failed
        return Ok(new { message = "If that email exists, a reset link was sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var invalid = BadRequest(new { message = "Invalid or expired reset link." });
        var user = await _users.FindByIdAsync(req.UserId);
        if (user == null) return invalid;

        string raw;
        try { raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Token)); }
        catch (FormatException) { return invalid; }

        // also runs the password policy; a bad token is the only error not shown to the user
        var result = await _users.ResetPasswordAsync(user, raw, req.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))) return invalid;
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // the new password signs every device out: no refresh tokens left, and access tokens die with their session
        await _sessions.RevokeAll(user.Id);
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        return Ok(new { message = "Password changed. You can log in now." });
    }

    async Task TrySendPasswordReset(AppUser user)
    {
        try { await _email.SendPasswordReset(_users, user); }
        catch (Exception ex)
        {
            _log.LogError(ex.GetType().Name + " sending reset email for user {UserId}", user.Id);
        }
    }

    // the exception can hold the link, so log only the user id
    async Task<bool> TrySendConfirmation(AppUser user)
    {
        try { await _email.SendConfirmation(_users, user); return true; }
        catch (Exception ex)
        {
            _log.LogError(ex.GetType().Name + " sending confirmation email for user {UserId}", user.Id);
            return false;
        }
    }

    // same body for login and refresh; the long-lived token only ever travels in the cookie
    async Task<object> SignedIn(AppUser user, Guid sessionId) => new
    {
        accessToken = await _sessions.AccessToken(user, sessionId),
        userId = user.Id, email = user.Email, userName = user.UserName, emailVerified = user.EmailConfirmed
    };
}
