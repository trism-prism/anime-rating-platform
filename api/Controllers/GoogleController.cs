using System.Security.Cryptography;
using System.Text;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace api.Controllers;

// Sign in with Google: authorization-code flow with PKCE, all on the server
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("auth")]
public class GoogleController : ControllerBase
{
    const string Provider = "Google";
    const string CookieName = "g_oauth"; // holds "state.verifier" between start and callback
    const string CookiePath = "/api/auth"; // the path the browser sees (Vercel rewrite, Vite proxy)

    readonly UserManager<AppUser> _users;
    readonly GoogleClient _google;
    readonly SessionService _sessions;
    readonly SecurityLog _events;
    readonly IConfiguration _config;
    readonly IHostEnvironment _env;

    public GoogleController(UserManager<AppUser> users, GoogleClient google, SessionService sessions, SecurityLog events, IConfiguration config, IHostEnvironment env)
    {
        _users = users;
        _google = google;
        _sessions = sessions;
        _events = events;
        _config = config;
        _env = env;
    }

    string Front => (_config["App:FrontendUrl"] ?? "").TrimEnd('/');
    string RedirectUri => Front + "/api/auth/google/callback";
    IActionResult Failed() => Redirect(Front + "/login?error=google");

    [HttpGet("providers")]
    public IActionResult Providers() => Ok(new { google = _google.Configured });

    [HttpGet("google/start")]
    public IActionResult Start()
    {
        if (!_google.Configured) return NotFound();
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        Response.Cookies.Append(CookieName, state + "." + verifier, CookieOptions(TimeSpan.FromMinutes(10)));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _google.ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account"
        };
        return Redirect(QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", query));
    }

    [HttpGet("google/callback")]
    public async Task<IActionResult> Callback(string? code, string? state)
    {
        var saved = Request.Cookies[CookieName]?.Split('.');
        Response.Cookies.Delete(CookieName, CookieOptions(null)); // one try per start
        if (!_google.Configured || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || saved is not [var savedState, var verifier]
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(savedState)))
            return Failed();

        var profile = await _google.Exchange(code, verifier, RedirectUri);
        if (profile == null) return Failed();

        var user = await _users.FindByLoginAsync(Provider, profile.Sub);
        var linking = false;
        if (user == null)
        {
            user = await _users.FindByEmailAsync(profile.Email);
            linking = user != null;
        }
        // an unconfirmed account may be a placeholder someone made (and locked) with this address; TakeOver clears it
        if (user is { EmailConfirmed: true } && await _users.IsLockedOutAsync(user)) return Failed();
        // the address changed hands: another Google account must not attach to this one
        if (linking && (await _users.GetLoginsAsync(user!)).Any(l => l.LoginProvider == Provider)) return Failed();

        if (user == null)
        {
            user = new AppUser { Email = profile.Email, UserName = await UserNames.FromEmail(_users, profile.Email), EmailConfirmed = true };
            if (!(await _users.CreateAsync(user)).Succeeded) return Failed(); // for example a second sign-up at the same moment
            if (!(await _users.AddLoginAsync(user, new UserLoginInfo(Provider, profile.Sub, Provider))).Succeeded)
            {
                await _users.DeleteAsync(user);
                return Failed();
            }
        }
        else if (linking)
        {
            // an unconfirmed account may have been made by someone who does not own the address: drop what they set up
            if (!user.EmailConfirmed) await TakeOver(user);
            if (!(await _users.AddLoginAsync(user, new UserLoginInfo(Provider, profile.Sub, Provider))).Succeeded) return Failed();
            user.EmailConfirmed = true; // Google verified the address
            await _users.UpdateAsync(user);
        }

        // Google is the strong factor here, so the local authenticator code is not asked for
        await AdminBootstrap.TryPromote(_users, _config, user);
        var (_, refreshToken) = await _sessions.Create(user, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        _sessions.SetCookie(Response, user.Id, refreshToken);
        if (linking) await _events.Add(user.Id, "google-linked", HttpContext);
        await _events.Add(user.Id, "login-google", HttpContext);
        return Redirect($"{Front}/auth/callback?userId={user.Id}");
    }

    // removes the password, two-factor setup and sessions of an account whose email was never confirmed
    async Task TakeOver(AppUser user)
    {
        if (await _users.HasPasswordAsync(user)) await _users.RemovePasswordAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        await _sessions.RevokeAll(user.Id);
    }

    // Lax: the callback is a top-level GET coming from Google. No Max-Age on delete (see SessionService).
    CookieOptions CookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = !_env.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = CookiePath,
        MaxAge = maxAge
    };
}
