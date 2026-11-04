using api.Data;
using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace api.Tests;

public class LockoutTests
{
    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(o =>
        {
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.Lockout.AllowedForNewUsers = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddSignInManager();

        using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var signIn = provider.GetRequiredService<SignInManager<AppUser>>();
        var user = new AppUser { UserName = "kai", Email = "kai@example.com" };
        Assert.True((await users.CreateAsync(user, "Correct1pass!")).Succeeded);

        for (var i = 0; i < 4; i++)
            Assert.False((await signIn.CheckPasswordSignInAsync(user, "wrong", true)).IsLockedOut);
        Assert.True((await signIn.CheckPasswordSignInAsync(user, "wrong", true)).IsLockedOut);
    }
}
