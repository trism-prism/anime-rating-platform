using api.Models;
using Microsoft.AspNetCore.Identity;

namespace api.Services;

public static class UserNames
{
    // the email's local part, plus 4 random digits if that name is taken
    public static async Task<string> FromEmail(UserManager<AppUser> users, string email)
    {
        var name = email.Split('@')[0];
        if (await users.FindByNameAsync(name) != null) name += Random.Shared.Next(1000, 10000);
        return name;
    }

    // accounts made before usernames were generated have the email as their username
    public static async Task FixEmailNames(UserManager<AppUser> users)
    {
        var bad = users.Users.Where(u => u.UserName != null && u.UserName.Contains("@")).ToList();
        foreach (var user in bad)
            await users.SetUserNameAsync(user, await FromEmail(users, user.UserName!));
    }
}
