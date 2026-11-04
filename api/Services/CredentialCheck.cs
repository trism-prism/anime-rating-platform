using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace api.Services;

public enum Check { Ok, Wrong, Locked }

// checks a password or a two-factor code; every miss counts toward the same lockout
public class CredentialCheck
{
    readonly UserManager<AppUser> _users;
    readonly SecurityLog _log;

    public CredentialCheck(UserManager<AppUser> users, SecurityLog log)
    {
        _users = users;
        _log = log;
    }

    public async Task<Check> Password(AppUser user, string password, HttpContext http)
    {
        if (await _users.IsLockedOutAsync(user)) return Check.Locked;
        if (await _users.CheckPasswordAsync(user, password)) return Check.Ok;
        return await Failed(user, http);
    }

    // an authenticator app code or an unused recovery code
    public async Task<Check> Code(AppUser user, string code, HttpContext http)
    {
        if (await _users.IsLockedOutAsync(user)) return Check.Locked;
        var c = (code ?? "").Replace(" ", "").ToUpperInvariant(); // recovery codes look like ABCDE-12345
        // an empty code would match the empty recovery list once every code is used
        if (c.Length > 0 && (await _users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, c)
            || (await _users.RedeemTwoFactorRecoveryCodeAsync(user, c)).Succeeded)) return Check.Ok;
        return await Failed(user, http);
    }

    async Task<Check> Failed(AppUser user, HttpContext http)
    {
        await _users.AccessFailedAsync(user);
        if (!await _users.IsLockedOutAsync(user)) return Check.Wrong;
        await _log.Add(user.Id, "locked-out", http);
        return Check.Locked;
    }

    public static ObjectResult Locked() =>
        new(new { message = "Too many failed attempts. Try again in 5 minutes." }) { StatusCode = 423 };

    // 403 rather than 401: the web client retries a 401 once with a fresh token, which would count the miss twice
    public static ObjectResult Refused(Check check, string wrong) =>
        check == Check.Locked ? Locked() : new(new { message = wrong }) { StatusCode = 403 };
}
