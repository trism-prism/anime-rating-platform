using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class SessionTests
{
    const string Pass = "Passw0rd1!";

    static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = new string('k', 40), ["Jwt:Issuer"] = "test" }).Build();

    static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        var dbName = Guid.NewGuid().ToString(); // one name, so scopes (one DbContext each) share the data
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    static SessionService Sessions(ServiceProvider p, string env = "Development") =>
        new(p.GetRequiredService<AppDbContext>(), p.GetRequiredService<UserManager<AppUser>>(), Config,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = env });

    static async Task<AppUser> AddUser(ServiceProvider p, string name)
    {
        var user = new AppUser { UserName = name, Email = name + "@example.com" };
        Assert.True((await p.GetRequiredService<UserManager<AppUser>>().CreateAsync(user, Pass)).Succeeded);
        return user;
    }

    static AuthController Auth(ServiceProvider p, HttpContext http, string env = "Development")
    {
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var host = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = env };
        return new AuthController(users,
            new EmailSender(Config, NullLogger<EmailSender>.Instance, host, new HttpClient()),
            Sessions(p, env), new CredentialCheck(users), new MemoryCache(new MemoryCacheOptions()), Config, NullLogger<AuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    // the browser would send back what Set-Cookie held
    static HttpContext WithCookie(string userId, string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{SessionService.CookieName(userId)}={token}";
        return http;
    }

    static string SetCookie(HttpContext http) => http.Response.Headers.SetCookie.ToString();
    static string CookieValue(HttpContext http) => SetCookie(http).Split('=', 2)[1].Split(';')[0];
    static JsonElement Body(ObjectResult r) => JsonSerializer.SerializeToElement(r.Value);

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (_, first) = await sessions.Create(user, "test", "1.2.3.4");

        var r1 = await sessions.Refresh(user.Id, first);
        Assert.NotNull(r1);
        Assert.NotEqual(first, r1.RefreshToken);
        var r2 = await sessions.Refresh(user.Id, r1.RefreshToken);
        Assert.NotNull(r2);
        Assert.Equal(r1.Session.Id, r2.Session.Id);
    }

    [Fact]
    public async Task Reusing_the_old_token_revokes_the_session()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (session, first) = await sessions.Create(user, "test", "1.2.3.4");
        var rotated = await sessions.Refresh(user.Id, first);

        Assert.Null(await sessions.Refresh(user.Id, first)); // the stolen copy
        Assert.False(await sessions.IsActive(session.Id.ToString()));
        Assert.Null(await sessions.Refresh(user.Id, rotated!.RefreshToken)); // so the real one is dead too
    }

    [Fact]
    public async Task Unknown_token_or_other_users_id_gets_nothing()
    {
        using var p = Build();
        var kai = await AddUser(p, "kai");
        var ann = await AddUser(p, "ann");
        var sessions = Sessions(p);
        var (_, token) = await sessions.Create(kai, "test", "1.2.3.4");

        Assert.Null(await sessions.Refresh(kai.Id, "nonsense"));
        Assert.Null(await sessions.Refresh(ann.Id, token));
        Assert.NotNull(await sessions.Refresh(kai.Id, token)); // still fine
    }

    [Fact]
    public async Task Revoked_session_access_token_is_rejected_but_works_before()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (session, _) = await sessions.Create(user, "test", "1.2.3.4");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(await sessions.AccessToken(user, session.Id));
        var sid = jwt.Claims.Single(c => c.Type == "sid").Value;
        Assert.Equal(user.Id, jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));

        // Program.cs calls this from JwtBearer's OnTokenValidated
        Assert.True(await sessions.IsActive(sid));
        await sessions.RevokeAll(user.Id);
        Assert.False(await sessions.IsActive(sid));
        Assert.False(await sessions.IsActive(null));
        Assert.False(await sessions.IsActive("not a guid"));
    }

    [Fact]
    public async Task Expired_session_cannot_refresh_or_authenticate()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (session, token) = await sessions.Create(user, "test", "1.2.3.4");
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await p.GetRequiredService<AppDbContext>().SaveChangesAsync();

        Assert.Null(await sessions.Refresh(user.Id, token));
        Assert.False(await sessions.IsActive(session.Id.ToString()));
    }

    [Fact]
    public async Task Changed_security_stamp_ends_the_session_at_refresh()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (session, token) = await sessions.Create(user, "test", "1.2.3.4");
        await p.GetRequiredService<UserManager<AppUser>>().UpdateSecurityStampAsync(user);

        Assert.Null(await sessions.Refresh(user.Id, token));
        Assert.False(await sessions.IsActive(session.Id.ToString()));
    }

    [Fact]
    public async Task Two_refreshes_racing_with_one_token_end_the_session()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var (session, token) = await Sessions(p).Create(user, "test", "1.2.3.4");
        using var a = p.CreateScope(); // each request has its own DbContext
        using var b = p.CreateScope();
        SessionService In(IServiceScope s) => new(s.ServiceProvider.GetRequiredService<AppDbContext>(),
            s.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), Config,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" });
        // b has read the session before a rotates it, as when two requests arrive together
        await b.ServiceProvider.GetRequiredService<AppDbContext>().Sessions.ToListAsync();

        Assert.NotNull(await In(a).Refresh(user.Id, token));
        Assert.Null(await In(b).Refresh(user.Id, token)); // the loser holds a used token
        Assert.False(await Sessions(p).IsActive(session.Id.ToString()));
    }

    [Fact]
    public async Task Sliding_expiry_stops_90_days_after_sign_in()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        var (session, token) = await sessions.Create(user, "test", "1.2.3.4");
        session.CreatedAt = DateTime.UtcNow.AddDays(-80);
        await p.GetRequiredService<AppDbContext>().SaveChangesAsync();

        var r = await sessions.Refresh(user.Id, token);

        Assert.InRange(r!.Session.ExpiresAt, DateTime.UtcNow.AddDays(9.9), DateTime.UtcNow.AddDays(10.1)); // not 30
    }

    [Fact]
    public async Task Refresh_and_logout_reject_an_id_that_is_not_a_guid()
    {
        using var p = Build();
        var auth = Auth(p, new DefaultHttpContext());
        Assert.IsType<BadRequestObjectResult>(await auth.Refresh(new UserIdRequest("../x")));
        Assert.IsType<BadRequestObjectResult>(await auth.Logout(new UserIdRequest("a;b")));
    }

    [Fact]
    public async Task User_agent_is_trimmed_to_200_characters()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var (session, _) = await Sessions(p).Create(user, new string('a', 500), null);
        Assert.Equal(200, session.UserAgent.Length);
    }

    [Fact]
    public async Task Login_sets_an_httponly_cookie_and_returns_only_the_access_token()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var http = new DefaultHttpContext();

        var res = Assert.IsType<OkObjectResult>(await Auth(p, http).Login(new LoginRequest("kai@example.com", Pass)));

        var body = Body(res);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.Equal(user.Id, body.GetProperty("userId").GetString());
        Assert.False(body.TryGetProperty("token", out _));
        var cookie = SetCookie(http).ToLowerInvariant();
        Assert.StartsWith(SessionService.CookieName(user.Id).ToLowerInvariant() + "=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/api/auth", cookie);
        Assert.DoesNotContain("secure", cookie); // http on a dev machine
        Assert.Single(await Sessions(p).Active(user.Id));
    }

    [Fact]
    public async Task Cookie_is_secure_outside_development()
    {
        using var p = Build();
        await AddUser(p, "kai");
        var http = new DefaultHttpContext();
        await Auth(p, http, Environments.Production).Login(new LoginRequest("kai@example.com", Pass));
        Assert.Contains("secure", SetCookie(http).ToLowerInvariant());
    }

    [Fact]
    public async Task Refresh_endpoint_rotates_the_cookie_and_rejects_a_missing_or_old_one()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var login = new DefaultHttpContext();
        await Auth(p, login).Login(new LoginRequest("kai@example.com", Pass));
        var first = CookieValue(login);

        var again = WithCookie(user.Id, first);
        var ok = Assert.IsType<OkObjectResult>(await Auth(p, again).Refresh(new UserIdRequest(user.Id)));
        Assert.False(string.IsNullOrEmpty(Body(ok).GetProperty("accessToken").GetString()));
        Assert.NotEqual(first, CookieValue(again));

        Assert.IsType<UnauthorizedObjectResult>(await Auth(p, new DefaultHttpContext()).Refresh(new UserIdRequest(user.Id))); // no cookie
        Assert.IsType<UnauthorizedObjectResult>(await Auth(p, WithCookie(user.Id, first)).Refresh(new UserIdRequest(user.Id))); // reuse
        Assert.IsType<UnauthorizedObjectResult>(await Auth(p, WithCookie(user.Id, CookieValue(again))).Refresh(new UserIdRequest(user.Id)));
        Assert.Empty(await Sessions(p).Active(user.Id));
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_deletes_the_cookie()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var login = new DefaultHttpContext();
        await Auth(p, login).Login(new LoginRequest("kai@example.com", Pass));

        var http = WithCookie(user.Id, CookieValue(login));
        Assert.IsType<NoContentResult>(await Auth(p, http).Logout(new UserIdRequest(user.Id)));

        Assert.Empty(await Sessions(p).Active(user.Id));
        var cookie = SetCookie(http).ToLowerInvariant();
        Assert.StartsWith(SessionService.CookieName(user.Id).ToLowerInvariant() + "=;", cookie);
        Assert.Contains("1970", cookie); // expired
        Assert.DoesNotContain("max-age", cookie); // browsers prefer it over expires
    }

    [Fact]
    public async Task Password_reset_revokes_every_session()
    {
        using var p = Build();
        var user = await AddUser(p, "kai");
        var sessions = Sessions(p);
        await sessions.Create(user, "phone", "1.1.1.1");
        await sessions.Create(user, "laptop", "2.2.2.2");
        var raw = await p.GetRequiredService<UserManager<AppUser>>().GeneratePasswordResetTokenAsync(user);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));

        Assert.IsType<OkObjectResult>(await Auth(p, new DefaultHttpContext()).ResetPassword(new ResetPasswordRequest(user.Id, token, "An0ther!pass")));

        Assert.Empty(await sessions.Active(user.Id));
    }

    [Fact]
    public async Task RevokeAll_keeps_the_excepted_session_and_leaves_other_users_alone()
    {
        using var p = Build();
        var kai = await AddUser(p, "kai");
        var ann = await AddUser(p, "ann");
        var sessions = Sessions(p);
        var (kaiPhone, _) = await sessions.Create(kai, "phone", "1.1.1.1");
        var (kaiLaptop, _) = await sessions.Create(kai, "laptop", "2.2.2.2");
        await sessions.Create(ann, "ann phone", "3.3.3.3");

        await sessions.RevokeAll(kai.Id, kaiPhone.Id);

        Assert.Equal(new[] { kaiPhone.Id }, (await sessions.Active(kai.Id)).Select(s => s.Id).ToArray());
        Assert.False(await sessions.IsActive(kaiLaptop.Id.ToString()));
        Assert.Single(await sessions.Active(ann.Id));
    }
}
