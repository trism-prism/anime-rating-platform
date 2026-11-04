using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace api.Tests;

public class UserNamesTests
{
    static UserManager<AppUser> Users()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider().GetRequiredService<UserManager<AppUser>>();
    }

    [Fact]
    public async Task Generator_returns_local_part_then_adds_digits_when_taken()
    {
        var users = Users();
        Assert.Equal("sam", await UserNames.FromEmail(users, "sam@example.com"));

        Assert.True((await users.CreateAsync(new AppUser { UserName = "sam", Email = "sam@example.com" })).Succeeded);
        var next = await UserNames.FromEmail(users, "sam@other.com");
        Assert.Matches(@"^sam\d{4}$", next);
    }

    [Fact]
    public async Task Startup_fix_renames_email_named_user()
    {
        var users = Users();
        Assert.True((await users.CreateAsync(new AppUser { UserName = "kim@example.com", Email = "kim@example.com" })).Succeeded);

        await UserNames.FixEmailNames(users);

        var user = await users.FindByEmailAsync("kim@example.com");
        Assert.Equal("kim", user!.UserName);
    }
}
