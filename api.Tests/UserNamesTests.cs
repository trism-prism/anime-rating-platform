using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

        await UserNames.FixEmailNames(users, NullLogger.Instance);

        var user = await users.FindByEmailAsync("kim@example.com");
        Assert.Equal("kim", user!.UserName);
    }

    [Fact]
    public async Task Disallowed_characters_are_dropped_and_the_name_is_valid()
    {
        var users = Users();
        var name = await UserNames.FromEmail(users, "o'brien@example.com");
        Assert.Equal("obrien", name);
        Assert.True((await users.CreateAsync(new AppUser { UserName = name, Email = "o@example.com" })).Succeeded);

        Assert.Equal("user", await UserNames.FromEmail(users, "'@example.com"));
    }

    [Fact]
    public async Task Suffixed_name_that_is_also_taken_still_yields_a_free_name()
    {
        var users = Users();
        Assert.True((await users.CreateAsync(new AppUser { UserName = "sam", Email = "a@example.com" })).Succeeded);
        var first = await UserNames.FromEmail(users, "sam@x.com");
        Assert.True((await users.CreateAsync(new AppUser { UserName = first, Email = "b@example.com" })).Succeeded);

        var second = await UserNames.FromEmail(users, "sam@y.com");
        Assert.Matches(@"^sam\d{4}$", second);
        Assert.Null(await users.FindByNameAsync(second));
    }
}
