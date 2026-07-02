using api.Data;
using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using api.Controllers;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task Register_defaults_username_to_email_local_part_and_retries_when_taken()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        using var provider = services.BuildServiceProvider();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Development };
        var config = new ConfigurationBuilder().Build();
        var controller = new AuthController(provider.GetRequiredService<UserManager<AppUser>>(),
            provider.GetRequiredService<SignInManager<AppUser>>(),
            new EmailSender(config, NullLogger<EmailSender>.Instance, env), config, NullLogger<AuthController>.Instance);

        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@a.com", "Passw0rd1!", null)));
        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@b.com", "Passw0rd1!", null)));

        var users = provider.GetRequiredService<UserManager<AppUser>>();
        Assert.Equal("kai", (await users.FindByEmailAsync("kai@a.com"))!.UserName);
        var second = (await users.FindByEmailAsync("kai@b.com"))!.UserName!;
        Assert.Matches(@"^kai\d{4}$", second);
    }

    [Fact]
    public async Task Register_still_succeeds_when_the_email_cannot_be_sent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        using var provider = services.BuildServiceProvider();
        // Production without Smtp:Password makes EmailSender throw
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Production };
        var config = new ConfigurationBuilder().Build();
        var controller = new AuthController(provider.GetRequiredService<UserManager<AppUser>>(),
            provider.GetRequiredService<SignInManager<AppUser>>(),
            new EmailSender(config, NullLogger<EmailSender>.Instance, env), config, NullLogger<AuthController>.Instance);

        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@a.com", "Passw0rd1!", null)));
        var resend = Assert.IsType<ObjectResult>(await controller.Resend(new EmailRequest("kai@a.com")));
        Assert.Equal(503, resend.StatusCode);
    }
}
