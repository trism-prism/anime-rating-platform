using Microsoft.AspNetCore.Identity;

namespace api.Services;

// the Identity options Program.cs uses; tests build their services from the same method
public static class IdentitySetup
{
    public static void Configure(IdentityOptions o)
    {
        o.SignIn.RequireConfirmedEmail = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 10;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Lockout.AllowedForNewUsers = true;
        // length only, no composition rules; breached passwords are refused by PwnedPasswordValidator
        o.Password.RequiredLength = 12;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
    }
}
