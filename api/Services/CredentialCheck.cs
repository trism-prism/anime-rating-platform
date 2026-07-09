using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace api.Services;

public enum Check { Ok, Wrong, Locked, NoPassword }

// checks a password; every miss counts toward the lockout
public class CredentialCheck
{
    readonly UserManager<AppUser> _users;

    public CredentialCheck(UserManager<AppUser> users) => _users = users;

    public async Task<Check> Password(AppUser user, string password)
    {
        if (await _users.IsLockedOutAsync(user)) return Check.Locked;
        if (!await _users.HasPasswordAsync(user)) return Check.NoPassword; // signs in with Google only; nothing to guess, so no lockout count
        if (await _users.CheckPasswordAsync(user, password)) return Check.Ok;
        return await Failed(user);
    }

    async Task<Check> Failed(AppUser user)
    {
        await _users.AccessFailedAsync(user);
        return await _users.IsLockedOutAsync(user) ? Check.Locked : Check.Wrong;
    }

    public static ObjectResult Locked() =>
        new(new { message = "Too many failed attempts. Try again in 5 minutes." }) { StatusCode = 423 };

    // 403 rather than 401: the web client retries a 401 once with a fresh token, which would count the miss twice
    public static ObjectResult Refused(Check check, string wrong) =>
        check == Check.Locked ? Locked()
        : check == Check.NoPassword ? new(new { message = "This account has no password. Use Forgot password on the login page to set one." }) { StatusCode = 403 }
        : new(new { message = wrong }) { StatusCode = 403 };
}
