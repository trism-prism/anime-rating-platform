using System.Security.Claims;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class VerificationTests
{
    static ServiceProvider Build(string? adminEmail = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Email"] = adminEmail, ["Jwt:Key"] = new string('k', 40), ["Jwt:Issuer"] = "test" }).Build());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(o => o.SignIn.RequireConfirmedEmail = false)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    static async Task<AppUser> AddUser(ServiceProvider p, string name, bool confirmed)
    {
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var roles = p.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));
        var user = new AppUser { UserName = name, Email = name + "@example.com", EmailConfirmed = confirmed };
        Assert.True((await users.CreateAsync(user, "Passw0rd1!")).Succeeded);
        return user;
    }

    static AuthController Auth(ServiceProvider p)
    {
        var config = p.GetRequiredService<IConfiguration>();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Development };
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        var events = new SecurityLog(db);
        return new AuthController(users,
            new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient()),
            new SessionService(db, users, config, env), new CredentialCheck(users, events), events, config, NullLogger<AuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } }; // login sets a cookie
    }

    static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(new[] { new Claim("sub", id) }, "test"));

    [Fact]
    public async Task Unverified_user_can_log_in_and_sees_emailVerified_false()
    {
        using var p = Build();
        await AddUser(p, "kai", false);
        var res = Assert.IsType<OkObjectResult>(await Auth(p).Login(new LoginRequest("kai@example.com", "Passw0rd1!")));
        Assert.Contains("emailVerified = False", res.Value!.ToString());
    }

    [Fact]
    public async Task Review_text_needs_a_verified_email_but_a_plain_rating_does_not()
    {
        using var p = Build();
        var db = p.GetRequiredService<AppDbContext>();
        db.Titles.Add(new Title { Id = 1, AniListId = 1, Name = "Test" });
        var unverified = await AddUser(p, "u", false);
        var verified = await AddUser(p, "v", true);
        await db.SaveChangesAsync();

        RatingsController As(AppUser u) => new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(u.Id) } } };

        var blocked = Assert.IsType<ObjectResult>(await As(unverified).Put(1, new RatingRequest(8, 8, 8, 8, "nice")));
        Assert.Equal(403, blocked.StatusCode);
        Assert.IsType<OkObjectResult>(await As(unverified).Put(1, new RatingRequest(8, 8, 8, 8, "   ")));
        Assert.IsType<OkObjectResult>(await As(verified).Put(1, new RatingRequest(8, 8, 8, 8, "nice")));
    }

    [Fact]
    public async Task Bootstrap_needs_a_confirmed_email_and_no_existing_admin()
    {
        using var p = Build("boss@example.com");
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var config = p.GetRequiredService<IConfiguration>();
        var boss = await AddUser(p, "boss", false);

        await AdminBootstrap.TryPromote(users, config, boss);
        Assert.False(await users.IsInRoleAsync(boss, "Admin"));

        boss.EmailConfirmed = true;
        await users.UpdateAsync(boss);
        await AdminBootstrap.TryPromote(users, config, boss);
        Assert.True(await users.IsInRoleAsync(boss, "Admin"));

        // an admin already exists, so a second user with the configured address (e.g. after a change) is not promoted
        await users.RemoveFromRoleAsync(boss, "Admin");
        var other = await AddUser(p, "other", true);
        await users.AddToRoleAsync(other, "Admin");
        await AdminBootstrap.TryPromote(users, config, boss);
        Assert.False(await users.IsInRoleAsync(boss, "Admin"));
    }

    [Fact]
    public async Task Demoting_the_last_admin_is_refused()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var a = await AddUser(p, "a", true);
        var b = await AddUser(p, "b", true);
        await users.AddToRoleAsync(a, "Admin");
        await users.AddToRoleAsync(b, "Admin");
        var admin = new AdminController(users);

        Assert.IsType<NoContentResult>(await admin.Demote(a.Id));
        Assert.IsType<BadRequestObjectResult>(await admin.Demote(b.Id));
        Assert.True(await users.IsInRoleAsync(b, "Admin"));
    }

    [Fact]
    public async Task Admin_filter_rejects_a_non_admin_and_allows_an_admin()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var plain = await AddUser(p, "plain", true);
        var boss = await AddUser(p, "boss", true);
        await users.AddToRoleAsync(boss, "Admin");

        async Task<IActionResult?> Run(AppUser u)
        {
            var http = new DefaultHttpContext { User = Principal(u.Id), RequestServices = p };
            var ctx = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
            await new AdminOnlyAttribute().OnAuthorizationAsync(ctx);
            return ctx.Result;
        }

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Run(plain)).StatusCode);
        Assert.Null(await Run(boss));
        await users.RemoveFromRoleAsync(boss, "Admin"); // demoted: the same token no longer works
        Assert.NotNull(await Run(boss));
    }

    [Fact]
    public async Task Verify_email_has_a_cooldown_and_skips_verified_users()
    {
        using var p = Build();
        var unverified = await AddUser(p, "u", false);
        var verified = await AddUser(p, "v", true);
        var config = p.GetRequiredService<IConfiguration>();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Development };
        var cache = p.GetRequiredService<IMemoryCache>();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        var events = new SecurityLog(db);
        AccountController As(AppUser u) => new(users,
            new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient()),
            new SessionService(db, users, config, env), new CredentialCheck(users, events), events, cache, NullLogger<AccountController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(u.Id) } } };

        Assert.IsType<OkObjectResult>(await As(verified).VerifyEmail());
        Assert.IsType<OkObjectResult>(await As(unverified).VerifyEmail());
        Assert.Equal(429, Assert.IsType<ObjectResult>(await As(unverified).VerifyEmail()).StatusCode);
    }
}
