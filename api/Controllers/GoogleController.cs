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
    readonly IConfiguration _config;
    readonly IHostEnvironment _env;
    readonly ILogger<GoogleController> _log;

    public GoogleController(UserManager<AppUser> users, GoogleClient google, SessionService sessions, IConfiguration config, IHostEnvironment env, ILogger<GoogleController> log)
    {
        _users = users;
        _google = google;
        _sessions = sessions;
        _config = config;
        _env = env;
        _log = log;
    }

    string Front => (_config["App:FrontendUrl"] ?? "").TrimEnd('/');
    string RedirectUri => Front + "/api/auth/google/callback";

    // the reason is a fixed code (never the code, tokens or email) so the log shows where a sign-in stopped;
    // details is for non-sensitive extras
    IActionResult Failed(string reason, string details = "")
    {
        _log.LogWarning("Google callback refused: {Reason}{Details}", reason, details);
        return Redirect(Front + "/login?error=google");
    }

    // for a missing g_oauth cookie: how many cookies came, where from (host only) and to which host.
    // Counts and hosts only, never cookie names or values.
    string CookieDiagnostics()
    {
        var referer = Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var r) ? r.Host : Request.Headers.ContainsKey("Referer") ? "unreadable" : "none";
        return $" (cookies={Request.Cookies.Count}, referer={SessionService.Trim(referer, 100)}, host={SessionService.Trim(Request.Host.ToString(), 100)})";
    }

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
        if (!_google.Configured) return Failed("not-configured");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Failed("no-code"); // Google sent an error, e.g. access_denied
        if (saved is not [var savedState, var verifier]) return Failed("no-cookie", CookieDiagnostics());
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(savedState)))
            return Failed("state-mismatch");

        var profile = await _google.Exchange(code, verifier, RedirectUri);
        if (profile == null) return Failed("exchange-failed"); // token request failed or the id_token claims were refused

        var user = await _users.FindByLoginAsync(Provider, profile.Sub);
        var linking = false;
        if (user == null)
        {
            user = await _users.FindByEmailAsync(profile.Email);
            linking = user != null;
        }
        // an unconfirmed account may be a placeholder someone made (and locked) with this address; TakeOver clears it
        if (user is { EmailConfirmed: true } && await _users.IsLockedOutAsync(user)) return Failed("locked");
        // the address changed hands: another Google account must not attach to this one
        if (linking && (await _users.GetLoginsAsync(user!)).Any(l => l.LoginProvider == Provider)) return Failed("linked-other");

        if (user == null)
        {
            user = new AppUser { Email = profile.Email, UserName = await UserNames.FromEmail(_users, profile.Email), EmailConfirmed = true };
            if (!(await _users.CreateAsync(user)).Succeeded) return Failed("create-failed"); // for example a second sign-up at the same moment
            if (!(await _users.AddLoginAsync(user, new UserLoginInfo(Provider, profile.Sub, Provider))).Succeeded)
            {
                await _users.DeleteAsync(user);
                return Failed("link-failed");
            }
        }
        else if (linking)
        {
            // a confirmed account is linked too: Google verified the same address
            if (!(await _users.AddLoginAsync(user, new UserLoginInfo(Provider, profile.Sub, Provider))).Succeeded) return Failed("link-failed");
            // an unconfirmed account may have been made by someone who does not own the address: drop what they set up
            // (after the link, so a failed link does not leave a wiped placeholder)
            if (!user.EmailConfirmed) await TakeOver(user);
            user.EmailConfirmed = true; // Google verified the address
            await _users.UpdateAsync(user);
        }

        await AdminBootstrap.TryPromote(_users, _config, user);
        var (_, refreshToken) = await _sessions.Create(user, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        _sessions.SetCookie(Response, user.Id, refreshToken);
        return Redirect($"{Front}/auth/callback?userId={user.Id}");
    }

    // removes the password and sessions of an account whose email was never confirmed
    async Task TakeOver(AppUser user)
    {
        if (await _users.HasPasswordAsync(user)) await _users.RemovePasswordAsync(user);
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
