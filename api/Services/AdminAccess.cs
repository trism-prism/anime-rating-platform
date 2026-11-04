using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace api.Services;

public static class AdminBootstrap
{
    // login needs no confirmation, so require a confirmed email or anyone could register the admin address first
    public static async Task TryPromote(UserManager<AppUser> users, IConfiguration config, AppUser user)
    {
        var adminEmail = config["Admin:Email"];
        if (string.IsNullOrEmpty(adminEmail) || !user.EmailConfirmed) return;
        if (!string.Equals(adminEmail, user.Email, StringComparison.OrdinalIgnoreCase)) return;
        if ((await users.GetUsersInRoleAsync("Admin")).Count > 0) return;
        await users.AddToRoleAsync(user, "Admin");
    }
}

// checks the role in the database on every request, so a demoted admin loses access at once
public class AdminOnlyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var id = ctx.HttpContext.User.FindFirst("sub")?.Value;
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = id == null ? null : await users.FindByIdAsync(id);
        if (user == null || !await users.IsInRoleAsync(user, "Admin")) ctx.Result = new StatusCodeResult(403);
    }
}
