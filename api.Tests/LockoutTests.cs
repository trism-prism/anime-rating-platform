using api.Data;
using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using api.Controllers;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class LockoutTests
{
    // lockout itself is covered in AuthFlowTests
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
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var db = provider.GetRequiredService<AppDbContext>();
        var controller = new AuthController(users,
            new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient()),
            new SessionService(db, users, config, env), new CredentialCheck(users), new MemoryCache(new MemoryCacheOptions()), config, NullLogger<AuthController>.Instance);

        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@a.com", "Passw0rd1!", null)));
        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@b.com", "Passw0rd1!", null)));

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
        // Production without Resend:ApiKey makes EmailSender throw
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Production };
        var config = new ConfigurationBuilder().Build();
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var db = provider.GetRequiredService<AppDbContext>();
        var controller = new AuthController(users,
            new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient()),
            new SessionService(db, users, config, env), new CredentialCheck(users), new MemoryCache(new MemoryCacheOptions()), config, NullLogger<AuthController>.Instance);

        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest("kai@a.com", "Passw0rd1!", null)));
        var resend = Assert.IsType<ObjectResult>(await controller.Resend(new EmailRequest("kai@a.com")));
        Assert.Equal(503, resend.StatusCode);
    }
}
