using api.Models;
using Microsoft.AspNetCore.Identity;

namespace api.Services;

public static class UserNames
{
    // the email's local part (only characters Identity allows), plus 4 random digits while that name is taken
    public static async Task<string> FromEmail(UserManager<AppUser> users, string email)
    {
        var local = new string(email.Split('@')[0].Where(c => char.IsAsciiLetterOrDigit(c) || "-._+".Contains(c)).ToArray());
        var name = local.Length > 0 ? local : "user";
        for (var i = 0; i < 10 && await users.FindByNameAsync(name) != null; i++)
            name = (local.Length > 0 ? local : "user") + Random.Shared.Next(1000, 10000);
        return name;
    }

    // accounts made before usernames were generated have the email as their username
    public static async Task FixEmailNames(UserManager<AppUser> users, ILogger log)
    {
        var bad = users.Users.Where(u => u.UserName != null && u.UserName.Contains("@")).ToList();
        foreach (var user in bad)
        {
            var result = await users.SetUserNameAsync(user, await FromEmail(users, user.UserName!));
            if (!result.Succeeded)
                log.LogWarning("Username fix failed for {Id}: {Errors}", user.Id, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
