using System.Net;
using System.Text;
using System.Text.Json;
using api.Controllers;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class AuthFlowTests
{
    const string Pass = "correct horse battery";

    class Hibp(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    // a clean password: HIBP returns only padding
    static Hibp Clean() => new("0018A45C4D1DEF81644B54AB7F969B88D65:0\r\n");

    static ServiceProvider Build(HttpMessageHandler hibp)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddHttpClient<PwnedPasswordValidator>(c => c.Timeout = TimeSpan.FromSeconds(3))
            .ConfigurePrimaryHttpMessageHandler(() => hibp);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(IdentitySetup.Configure).AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        // after AddIdentityCore, as in Program.cs, so the built-in length rule stays
        services.AddScoped<IPasswordValidator<AppUser>>(sp => sp.GetRequiredService<PwnedPasswordValidator>());
        return services.BuildServiceProvider();
    }

    // Production with no mail settings makes the send throw
    static AuthController Auth(ServiceProvider p, string env = "Development")
    {
        var config = new ConfigurationBuilder().Build();
        var host = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = env };
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        var events = new SecurityLog(db);
        return new AuthController(users,
            new EmailSender(config, NullLogger<EmailSender>.Instance, host, new HttpClient()),
            new SessionService(db, users, config, host), new CredentialCheck(users, events), events, config, NullLogger<AuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } }; // the lockout event reads the request
    }

    static async Task<AppUser> AddUser(ServiceProvider p)
    {
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = "kai", Email = "kai@example.com" };
        Assert.True((await users.CreateAsync(user, Pass)).Succeeded);
        return user;
    }

    // anonymous result bodies are internal, so read them as JSON
    static JsonElement Body(ObjectResult r) => JsonSerializer.SerializeToElement(r.Value);
    static string Message(ObjectResult r) => Body(r).GetProperty("message").GetString()!;

    static async Task<string> ResetToken(ServiceProvider p, AppUser user)
    {
        var raw = await p.GetRequiredService<UserManager<AppUser>>().GeneratePasswordResetTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    [Fact]
    public async Task Login_shows_tries_left_only_near_the_limit_then_locks_with_423()
    {
        using var p = Build(Clean());
        await AddUser(p);
        var auth = Auth(p);
        var bad = new LoginRequest("kai@example.com", "wrong");

        // failures 1-6: plain message; failure 7 leaves 3
        for (var i = 1; i <= 6; i++)
            Assert.Equal("Invalid email or password.", Message(Assert.IsType<UnauthorizedObjectResult>(await auth.Login(bad))));
        Assert.Equal("Invalid email or password. 3 tries left.", Message(Assert.IsType<UnauthorizedObjectResult>(await auth.Login(bad))));
        Assert.Equal("Invalid email or password. 2 tries left.", Message(Assert.IsType<UnauthorizedObjectResult>(await auth.Login(bad))));
        Assert.Equal("Invalid email or password. 1 tries left.", Message(Assert.IsType<UnauthorizedObjectResult>(await auth.Login(bad))));

        // the 10th failure locks
        var locked = Assert.IsType<ObjectResult>(await auth.Login(bad));
        Assert.Equal(423, locked.StatusCode);
        Assert.Equal("Too many failed attempts. Try again in 5 minutes.", Message(locked));
        // even the right password is refused while locked
        Assert.Equal(423, Assert.IsType<ObjectResult>(await auth.Login(new LoginRequest("kai@example.com", Pass))).StatusCode);
    }

    // Program.cs and the tests share IdentitySetup, so this pins what DEPLOY.md promises
    [Fact]
    public void Lockout_options_are_10_attempts_for_5_minutes()
    {
        var o = new IdentityOptions();
        IdentitySetup.Configure(o);
        Assert.Equal(10, o.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), o.Lockout.DefaultLockoutTimeSpan);
        Assert.True(o.Lockout.AllowedForNewUsers);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_gets_the_plain_message()
    {
        using var p = Build(Clean());
        var res = Assert.IsType<UnauthorizedObjectResult>(await Auth(p).Login(new LoginRequest("nobody@example.com", "x")));
        Assert.Equal("Invalid email or password.", Message(res));
    }

    [Fact]
    public async Task Forgot_password_gives_the_same_reply_for_known_unknown_and_failed_send()
    {
        using var p = Build(Clean());
        await AddUser(p);

        var known = Assert.IsType<OkObjectResult>(await Auth(p).ForgotPassword(new EmailRequest("kai@example.com")));
        var unknown = Assert.IsType<OkObjectResult>(await Auth(p).ForgotPassword(new EmailRequest("nobody@example.com")));
        var failedSend = Assert.IsType<OkObjectResult>(await Auth(p, Environments.Production).ForgotPassword(new EmailRequest("kai@example.com")));

        Assert.Equal("If that email exists, a reset link was sent.", Message(known));
        Assert.Equal(Message(known), Message(unknown));
        Assert.Equal(Message(known), Message(failedSend));
    }

    [Fact]
    public async Task Reset_password_changes_it_clears_lockout_and_the_token_works_once()
    {
        using var p = Build(Clean());
        var user = await AddUser(p);
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var auth = Auth(p);
        for (var i = 0; i < 10; i++) await auth.Login(new LoginRequest("kai@example.com", "wrong"));
        Assert.True(await users.IsLockedOutAsync(user));

        var token = await ResetToken(p, user);
        Assert.IsType<OkObjectResult>(await auth.ResetPassword(new ResetPasswordRequest(user.Id, token, "a brand new passphrase")));

        Assert.False(await users.IsLockedOutAsync(user));
        Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
        Assert.True(await users.CheckPasswordAsync(user, "a brand new passphrase"));
        // the security stamp changed, so the same link is dead
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new ResetPasswordRequest(user.Id, token, "another new passphrase")));
    }

    [Fact]
    public async Task Reset_password_with_a_bad_token_or_user_gives_the_generic_error()
    {
        using var p = Build(Clean());
        var user = await AddUser(p);
        var auth = Auth(p);

        foreach (var req in new[]
        {
            new ResetPasswordRequest(user.Id, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("junk")), "a brand new passphrase"),
            new ResetPasswordRequest(user.Id, "!!not base64!!", "a brand new passphrase"),
            new ResetPasswordRequest("missing", "abc", "a brand new passphrase"),
        })
            Assert.Equal("Invalid or expired reset link.", Message(Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(req))));
    }

    [Fact]
    public async Task Reset_password_applies_the_password_policy()
    {
        using var p = Build(Clean());
        var user = await AddUser(p);
        var res = Assert.IsType<BadRequestObjectResult>(await Auth(p).ResetPassword(new ResetPasswordRequest(user.Id, await ResetToken(p, user), "short")));
        Assert.Contains("at least 12", Body(res).GetProperty("errors").ToString());
    }

    [Fact]
    public async Task Breached_password_is_refused_and_only_the_hash_prefix_is_sent()
    {
        using var p = Build(Clean());
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes("password12345")));
        var breached = new Hibp($"{sha[5..]}:42\r\n");
        var result = await new PwnedPasswordValidator(new HttpClient(breached), NullLogger<PwnedPasswordValidator>.Instance)
            .ValidateAsync(users, new AppUser(), "password12345");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "PwnedPassword");
        Assert.Equal("https://api.pwnedpasswords.com/range/" + sha[..5], breached.Request!.RequestUri!.ToString());
        Assert.Equal("true", breached.Request.Headers.GetValues("Add-Padding").Single());
    }

    [Fact]
    public async Task Create_user_is_refused_for_a_breached_password()
    {
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(Pass)));
        using var p = Build(new Hibp($"{sha[5..]}:1\r\n"));
        var result = await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "a", Email = "a@example.com" }, Pass);
        Assert.Contains(result.Errors, e => e.Description == "This password appears in a known data breach. Pick another one.");
    }

    [Fact]
    public void Validator_is_wired_like_Program_cs_with_the_3_second_timeout()
    {
        // same registrations as Program.cs, with scope and build validation on
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddHttpClient<EmailSender>();
        services.AddHttpClient<PwnedPasswordValidator>(c => c.Timeout = TimeSpan.FromSeconds(3));
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        services.AddScoped<IPasswordValidator<AppUser>>(sp => sp.GetRequiredService<PwnedPasswordValidator>());
        using var p = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = p.CreateScope();

        scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var validator = scope.ServiceProvider.GetServices<IPasswordValidator<AppUser>>().OfType<PwnedPasswordValidator>().Single();
        var http = (HttpClient)typeof(PwnedPasswordValidator).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(validator)!;
        Assert.Equal(TimeSpan.FromSeconds(3), http.Timeout);
    }

    [Fact]
    public async Task Clean_password_is_allowed()
    {
        using var p = Build(Clean());
        Assert.True((await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "a", Email = "a@example.com" }, Pass)).Succeeded);
    }

    [Fact]
    public async Task Padding_lines_with_a_zero_count_are_not_a_match()
    {
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(Pass)));
        using var p = Build(new Hibp($"{sha[5..]}:0\r\n"));
        Assert.True((await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "a", Email = "a@example.com" }, Pass)).Succeeded);
    }

    [Fact]
    public async Task Unreachable_hibp_allows_the_password()
    {
        using var p = Build(new Hibp("", HttpStatusCode.ServiceUnavailable));
        Assert.True((await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "a", Email = "a@example.com" }, Pass)).Succeeded);
    }

    [Fact]
    public async Task Short_password_is_rejected_by_length()
    {
        using var p = Build(Clean());
        var result = await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "a", Email = "a@example.com" }, "elevenchars");
        Assert.Contains(result.Errors, e => e.Code == "PasswordTooShort");
    }
}
