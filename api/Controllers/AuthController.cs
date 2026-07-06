using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace api.Controllers;

public record RegisterRequest(string Email, string Password, string? UserName);
public record EmailRequest(string Email);
public record LoginRequest(string Email, string Password);

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("auth")]
public class AuthController : ControllerBase
{
    readonly UserManager<AppUser> _users;
    readonly SignInManager<AppUser> _signIn;
    readonly EmailSender _email;
    readonly IConfiguration _config;
    readonly ILogger<AuthController> _log;

    public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn, EmailSender email, IConfiguration config, ILogger<AuthController> log)
    {
        _log = log;
        _users = users;
        _signIn = signIn;
        _email = email;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        // default the public name to the email's local part so reviews don't show the email
        var name = req.UserName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = req.Email.Split('@')[0];
            if (await _users.FindByNameAsync(name) != null) name += Random.Shared.Next(1000, 10000);
        }
        var user = new AppUser { Email = req.Email, UserName = name };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        if (!await TrySendConfirmation(user))
            return Ok(new { message = "Account created, but the confirmation email could not be sent. Use resend on the login page later." });
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
        if (result.IsLockedOut) return StatusCode(423, new { message = "Too many failed attempts. Try again in 15 minutes." });
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid email or password." });

        await AdminBootstrap.TryPromote(_users, _config, user);
        return Ok(new { token = await CreateToken(user), email = user.Email, userName = user.UserName, emailVerified = user.EmailConfirmed });
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

    async Task<string> CreateToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!)
        };
        claims.AddRange((await _users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
