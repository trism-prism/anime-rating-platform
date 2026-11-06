using System.Text;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

public record RegisterRequest(string Email, string Password, string? UserName, bool AgeConfirmed = false, bool TermsAccepted = false);
public record EmailRequest(string Email);
public record LoginRequest(string Email, string Password);
public record UserIdRequest(string UserId);
public record ResetCodeRequest(string Email, string Code);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record ConfirmEmailChangeRequest(string UserId, string NewEmail, string Token);

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("auth")]
public class AuthController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly EmailSender _email;
    readonly SessionService _sessions;
    readonly CredentialCheck _check;
    readonly IMemoryCache _cache;
    readonly IConfiguration _config;
    readonly ILogger<AuthController> _log;

    public AuthController(UserManager<AppUser> users, EmailSender email, SessionService sessions, CredentialCheck check, IMemoryCache cache, IConfiguration config, ILogger<AuthController> log)
    {
        _log = log;
        _users = users;
        _email = email;
        _sessions = sessions;
        _check = check;
        _cache = cache;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (!req.AgeConfirmed || !req.TermsAccepted)
            return BadRequest(new { message = "Confirm that you are 13 or older and accept the Terms of Service." });
        // default the public name to the email's local part so reviews don't show the email
        var name = req.UserName;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('@')) name = await UserNames.FromEmail(_users, req.Email);
        var user = new AppUser { Email = req.Email, UserName = name, TermsAcceptedAt = DateTime.UtcNow, TermsVersion = Terms.Version };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        // the email is verified later, with a code (see AccountController.SendVerifyCode)
        return Ok(new { message = "Account created. Log in, then verify your email to write reviews." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user == null) return Unauthorized(new { message = "Invalid email or password." });

        var check = await _check.Password(user, req.Password);
        if (check == Check.Locked) return CredentialCheck.Locked();
        // same answer as a wrong password, so the login form does not reveal which accounts use Google
        if (check == Check.NoPassword) return Unauthorized(new { message = "Invalid email or password." });
        if (check == Check.Wrong)
        {
            // warn only when the lockout is close
            var left = _users.Options.Lockout.MaxFailedAccessAttempts - user.AccessFailedCount;
            if (left <= 3) return Unauthorized(new { message = $"Invalid email or password. {left} tries left." });
            return Unauthorized(new { message = "Invalid email or password." });
        }

        // a right password is all it takes: no code is asked at login
        await _users.ResetAccessFailedCountAsync(user);
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
        if (!Guid.TryParse(req.UserId, out _)) return BadRequest(new { message = "Invalid user id." }); // it names a cookie
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
        if (!Guid.TryParse(req.UserId, out _)) return BadRequest(new { message = "Invalid user id." });
        var token = Request.Cookies[SessionService.CookieName(req.UserId)];
        if (token != null) await _sessions.RevokeByToken(req.UserId, token);
        _sessions.DeleteCookie(Response, req.UserId);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest req)
    {
        var user = await _users.FindByEmailAsync((req.Email ?? "").Trim());
        if (user != null && !CoolingDown("forgot-password", req.Email)) await TrySendResetCode(user);
        // same answer whether or not the email exists, or the send failed
        return Ok(new { message = "If that email has an account, a code was sent." });
    }

    // step two of the reset: is the emailed code right? Passwordless (Google) accounts use it too, to set a password.
    [HttpPost("reset-code/verify")]
    public async Task<IActionResult> VerifyResetCode(ResetCodeRequest req)
    {
        var key = TriesKey(req.Email);
        if (!EmailCodes.Try(_cache, key)) return TooManyTries();
        var ok = await ResetUser(req.Email, req.Code) != null;
        if (ok) EmailCodes.Clear(_cache, key); // the next step checks the code again
        return Ok(new { ok });
    }

    // step three: the code once more, then the new password (the policy and breach check apply)
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var key = TriesKey(req.Email);
        if (!EmailCodes.Try(_cache, key)) return TooManyTries();
        var user = await ResetUser(req.Email, req.Code);
        if (user == null) return BadRequest(new { message = "Invalid or expired code." });
        EmailCodes.Clear(_cache, key); // a right code gives the tries back, so a weak password can be retried

        var result = await _users.ResetPasswordAsync(user, await _users.GeneratePasswordResetTokenAsync(user), req.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        // owning the inbox proves the address; the new password also ends any lockout
        user.EmailConfirmed = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await _users.UpdateAsync(user);
        // every device signs out: no refresh tokens left, and access tokens die with their session
        await _sessions.RevokeAll(user.Id);
        return Ok(new { message = "Password changed. Log in with it." });
    }

    // the account when the address exists and the code is right for a reset, else null (same for both causes)
    async Task<AppUser?> ResetUser(string? email, string? code)
    {
        var user = await _users.FindByEmailAsync((email ?? "").Trim());
        return user != null && await EmailCodes.Right(_users, user, EmailCodes.Reset, code) ? user : null;
    }

    string TriesKey(string? email) => "reset-tries:" + Normalized(email);
    static ObjectResult TooManyTries() => new(new { message = "Too many attempts, wait 15 minutes." }) { StatusCode = 429 };

    // the link from the email sent to the new address (see AccountController.ChangeEmail)
    [HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest req)
    {
        var invalid = BadRequest(new { message = "Invalid or expired link." });
        var user = await _users.FindByIdAsync(req.UserId);
        if (user == null) return invalid;

        string raw;
        try { raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Token)); }
        catch (FormatException) { return invalid; }

        var result = await _users.ChangeEmailAsync(user, req.NewEmail, raw);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.InvalidEmail)))
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            return invalid;
        }

        // the new address changes the security stamp anyway, so every device signs in again
        await _sessions.RevokeAll(user.Id);
        return Ok(new { message = "Email changed. Log in again with the new address." });
    }

    // the address as Identity looks it up (so odd spellings of one address share a key)
    string Normalized(string? email) => _users.NormalizeEmail((email ?? "").Trim()) ?? "";

    // 60 s per action and address; true when one was already sent, else starts the cooldown
    bool CoolingDown(string action, string? email)
    {
        var key = $"{action}:{Normalized(email)}";
        if (_cache.TryGetValue(key, out _)) return true;
        _cache.Set(key, true, TimeSpan.FromSeconds(60));
        return false;
    }

    // the exception can hold the code, so log only the user id
    async Task TrySendResetCode(AppUser user)
    {
        try { await _email.SendResetCode(_users, user); }
        catch (Exception ex)
        {
            _log.LogError(ex.GetType().Name + " sending reset code for user {UserId}", user.Id);
        }
    }

    // same body for login and refresh; the long-lived token only ever travels in the cookie
    async Task<object> SignedIn(AppUser user, Guid sessionId) => new
    {
        accessToken = await _sessions.AccessToken(user, sessionId),
        userId = user.Id, email = user.Email, userName = user.UserName, emailVerified = user.EmailConfirmed
    };
}
