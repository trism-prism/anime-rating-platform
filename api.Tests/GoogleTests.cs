using System.Net;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class GoogleTests
{
    const string Front = "http://localhost:5173";
    const string Failed = Front + "/login?error=google";

    static IConfiguration Config(bool google = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = new string('k', 40), ["Jwt:Issuer"] = "test", ["App:FrontendUrl"] = Front,
        ["Google:ClientId"] = google ? "gid" : "", ["Google:ClientSecret"] = google ? "gsecret" : ""
    }).Build();

    // answers the token endpoint with an unsigned id_token and remembers the call
    class TokenHandler(string idToken) : HttpMessageHandler
    {
        public int Calls;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id_token = idToken })) };
        }
    }

    static string B64(object o) => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(o));

    static string IdToken(string email, string sub = "g-1", bool verified = true, string aud = "gid", long? exp = null) =>
        B64(new { alg = "none" }) + "." + B64(new
        {
            iss = "https://accounts.google.com", aud, sub, email, email_verified = verified,
            exp = exp ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        }) + ".";

    static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(IdentitySetup.Configure).AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    // collects what the controller logs, to check that every refusal names its reason
    class Reasons : ILogger<GoogleController>
    {
        public List<string> Lines = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(logLevel + " " + formatter(state, exception));
    }

    static GoogleController Make(ServiceProvider p, HttpMessageHandler handler, HttpContext http, bool google = true, Reasons? log = null)
    {
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Development };
        var config = Config(google);
        return new GoogleController(users, new GoogleClient(new HttpClient(handler), config, NullLogger<GoogleClient>.Instance),
            new SessionService(db, users, config, env), config, env, log ?? (ILogger<GoogleController>)NullLogger<GoogleController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    // a browser coming back from Google with the cookie that start set
    static (HttpContext Http, string State) Back(string state = "state123")
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"g_oauth={state}.verifier456";
        return (http, state);
    }

    static async Task<string> Callback(ServiceProvider p, string idToken, HttpContext? http = null, string state = "state123", Reasons? log = null)
    {
        var (ctx, _) = Back();
        var r = (RedirectResult)await Make(p, new TokenHandler(idToken), http ?? ctx, log: log).Callback("code1", state);
        return r.Url;
    }

    [Fact]
    public void Start_redirects_with_state_and_s256_challenge_and_sets_cookie()
    {
        using var p = Build();
        var http = new DefaultHttpContext();
        var r = (RedirectResult)Make(p, new TokenHandler(""), http).Start();

        var uri = new Uri(r.Url);
        Assert.Equal("accounts.google.com", uri.Host);
        var q = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("gid", q["client_id"]);
        Assert.Equal(Front + "/api/auth/google/callback", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("openid email profile", q["scope"]);
        Assert.Equal("S256", q["code_challenge_method"]);

        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.StartsWith("g_oauth=", cookie);
        Assert.Contains("httponly", cookie.ToLower());
        Assert.Contains("samesite=lax", cookie.ToLower());
        Assert.Contains("path=/api/auth", cookie.ToLower());
        var parts = cookie.Split('=', 2)[1].Split(';')[0].Split('.'); // state.verifier
        Assert.Equal(q["state"], parts[0]);
        Assert.Equal(WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(parts[1]))), q["code_challenge"]);
    }

    [Fact]
    public void Start_and_providers_follow_the_config()
    {
        using var p = Build();
        Assert.IsType<NotFoundResult>(Make(p, new TokenHandler(""), new DefaultHttpContext(), google: false).Start());
        Assert.Equal("{\"google\":false}", JsonSerializer.Serialize(((OkObjectResult)Make(p, new TokenHandler(""), new DefaultHttpContext(), google: false).Providers()).Value));
        Assert.Equal("{\"google\":true}", JsonSerializer.Serialize(((OkObjectResult)Make(p, new TokenHandler(""), new DefaultHttpContext()).Providers()).Value));
    }

    [Fact]
    public async Task Bad_state_is_refused_before_google_is_called()
    {
        using var p = Build();
        var handler = new TokenHandler(IdToken("a@example.com"));
        var log = new Reasons();
        var r = (RedirectResult)await Make(p, handler, Back().Http, log: log).Callback("code1", "other-state");

        Assert.Equal(Failed, r.Url);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(p.GetRequiredService<UserManager<AppUser>>().Users);

        // no cookie at all
        r = (RedirectResult)await Make(p, handler, new DefaultHttpContext(), log: log).Callback("code1", "state123");
        Assert.Equal(Failed, r.Url);

        // Google sent back an error instead of a code
        r = (RedirectResult)await Make(p, handler, Back().Http, log: log).Callback(null, null);
        Assert.Equal(Failed, r.Url);

        Assert.Equal(3, log.Lines.Count);
        Assert.Equal("Warning Google callback refused: state-mismatch", log.Lines[0]);
        Assert.StartsWith("Warning Google callback refused: no-cookie (cookies=0,", log.Lines[1]);
        Assert.Equal("Warning Google callback refused: no-code", log.Lines[2]);
    }

    [Fact]
    public async Task A_missing_cookie_is_logged_with_counts_and_hosts_only()
    {
        using var p = Build();
        var log = new Reasons();
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "rt_secret-user-id=secret-token; theme=dark"; // two other cookies, no g_oauth
        http.Request.Headers.Referer = "https://accounts.google.com/o/oauth2/v2/auth?client_id=gid&state=abc";
        http.Request.Host = new HostString("app.example.com");
        await Make(p, new TokenHandler(""), http, log: log).Callback("code1", "state123");

        Assert.Equal("Warning Google callback refused: no-cookie (cookies=2, referer=accounts.google.com, host=app.example.com)", Assert.Single(log.Lines));

        // no Referer header at all
        log.Lines.Clear();
        var bare = new DefaultHttpContext();
        bare.Request.Host = new HostString("app.example.com");
        await Make(p, new TokenHandler(""), bare, log: log).Callback("code1", "state123");
        Assert.Equal("Warning Google callback refused: no-cookie (cookies=0, referer=none, host=app.example.com)", Assert.Single(log.Lines));
    }

    [Fact]
    public async Task New_google_user_is_created_confirmed_without_password_and_gets_a_session()
    {
        using var p = Build();
        var handler = new TokenHandler(IdToken("Sam@Example.com"));
        var (http, _) = Back();
        var r = (RedirectResult)await Make(p, handler, http).Callback("code1", "state123");

        var users = p.GetRequiredService<UserManager<AppUser>>();
        var user = Assert.Single(users.Users);
        Assert.Equal($"{Front}/auth/callback?userId={user.Id}", r.Url);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("Sam", user.UserName);
        Assert.False(await users.HasPasswordAsync(user));
        Assert.NotNull(await users.FindByLoginAsync("Google", "g-1"));
        Assert.Contains("code_verifier=verifier456", handler.Body);

        var cookies = http.Response.Headers.SetCookie.ToString();
        Assert.Contains($"rt_{user.Id}=", cookies);
        Assert.Contains("g_oauth=;", cookies); // the one-time cookie is deleted
        Assert.Single(await p.GetRequiredService<AppDbContext>().Sessions.ToListAsync());

        // coming back later finds the same account
        Assert.StartsWith(Front + "/auth/callback", await Callback(p, IdToken("Sam@Example.com")));
        Assert.Single(users.Users);
    }

    [Fact]
    public async Task Existing_user_with_the_same_verified_email_is_linked_not_duplicated()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var local = new AppUser { UserName = "kai", Email = "kai@example.com", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(local, "correct horse battery")).Succeeded);

        var url = await Callback(p, IdToken("kai@example.com"));

        Assert.Equal($"{Front}/auth/callback?userId={local.Id}", url);
        Assert.Single(users.Users);
        Assert.Equal(local.Id, (await users.FindByLoginAsync("Google", "g-1"))!.Id);
        Assert.True(await users.HasPasswordAsync(local)); // a confirmed account keeps its password
    }

    [Fact]
    public async Task Linking_an_unconfirmed_account_confirms_it_and_drops_the_password_and_sessions()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var planted = new AppUser { UserName = "kai", Email = "kai@example.com" };
        Assert.True((await users.CreateAsync(planted, "correct horse battery")).Succeeded);
        var db = p.GetRequiredService<AppDbContext>();
        var old = new SessionService(db, users, Config(), new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" });
        var (attackerSession, _) = await old.Create(planted, "test", "1.2.3.4");

        Assert.StartsWith(Front + "/auth/callback", await Callback(p, IdToken("kai@example.com")));

        var user = (await users.FindByIdAsync(planted.Id))!;
        Assert.True(user.EmailConfirmed);
        Assert.False(await users.HasPasswordAsync(user));
        Assert.False(await old.IsActive(attackerSession.Id.ToString()));
    }

    [Theory]
    [InlineData(false, "gid", 300)]   // email not verified
    [InlineData(true, "other", 300)]  // issued to another app
    [InlineData(true, "gid", -300)]   // expired
    public async Task Bad_id_token_is_refused_and_creates_nothing(bool verified, string aud, int expiresIn)
    {
        using var p = Build();
        var token = IdToken("a@example.com", verified: verified, aud: aud, exp: DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds());
        var log = new Reasons();

        Assert.Equal(Failed, await Callback(p, token, log: log));
        Assert.Equal(new[] { "Warning Google callback refused: exchange-failed" }, log.Lines);
        Assert.Empty(p.GetRequiredService<UserManager<AppUser>>().Users);
        Assert.Empty(p.GetRequiredService<AppDbContext>().Sessions);
    }

    [Fact]
    public async Task Locked_out_user_is_refused_and_not_linked()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var local = new AppUser { UserName = "kai", Email = "kai@example.com", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(local, "correct horse battery")).Succeeded);
        await users.SetLockoutEndDateAsync(local, DateTimeOffset.UtcNow.AddMinutes(5));

        var log = new Reasons();
        Assert.Equal(Failed, await Callback(p, IdToken("kai@example.com"), log: log));
        Assert.Equal(new[] { "Warning Google callback refused: locked" }, log.Lines);
        Assert.Null(await users.FindByLoginAsync("Google", "g-1"));
        Assert.Empty(p.GetRequiredService<AppDbContext>().Sessions);
    }

    [Fact]
    public async Task Locked_placeholder_account_does_not_block_the_real_owner()
    {
        using var p = Build();
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var planted = new AppUser { UserName = "kai", Email = "kai@example.com" }; // never confirmed
        Assert.True((await users.CreateAsync(planted, "correct horse battery")).Succeeded);
        await users.SetLockoutEndDateAsync(planted, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.StartsWith(Front + "/auth/callback", await Callback(p, IdToken("kai@example.com")));
        Assert.False(await users.IsLockedOutAsync((await users.FindByIdAsync(planted.Id))!));
    }

    [Fact]
    public async Task A_second_google_account_cannot_attach_to_a_linked_user()
    {
        using var p = Build();
        await Callback(p, IdToken("kai@example.com", sub: "g-1"));

        var log = new Reasons();
        Assert.Equal(Failed, await Callback(p, IdToken("kai@example.com", sub: "g-2"), log: log));
        Assert.Equal(new[] { "Warning Google callback refused: linked-other" }, log.Lines);
        Assert.Null(await p.GetRequiredService<UserManager<AppUser>>().FindByLoginAsync("Google", "g-2"));
    }

    [Fact]
    public async Task Password_login_for_a_google_only_account_is_a_plain_401()
    {
        using var p = Build();
        await Callback(p, IdToken("sam@example.com"));
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var db = p.GetRequiredService<AppDbContext>();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Development };
        var auth = new AuthController(users, new EmailSender(Config(), NullLogger<EmailSender>.Instance, env, new HttpClient()),
            new SessionService(db, users, Config(), env), new CredentialCheck(users), new MemoryCache(new MemoryCacheOptions()), Config(), NullLogger<AuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var r = Assert.IsType<UnauthorizedObjectResult>(await auth.Login(new LoginRequest("sam@example.com", "anything at all")));
        Assert.Contains("Invalid email or password.", JsonSerializer.Serialize(r.Value));
        Assert.Single(db.Sessions); // only the Google one
    }

    [Fact]
    public async Task Password_checks_fail_normally_for_an_account_without_a_password()
    {
        using var p = Build();
        await Callback(p, IdToken("sam@example.com"));
        var users = p.GetRequiredService<UserManager<AppUser>>();
        var user = users.Users.Single();
        var check = new CredentialCheck(users);

        for (var i = 0; i < 15; i++)
            Assert.Equal(Check.NoPassword, await check.Password(user, "anything at all"));
        Assert.False(await users.IsLockedOutAsync(user)); // nothing to guess, so no lockout

        var refused = CredentialCheck.Refused(Check.NoPassword, "Wrong password.");
        Assert.Equal(403, refused.StatusCode);
        Assert.Contains("no password", JsonSerializer.Serialize(refused.Value));
        Assert.Contains("Wrong password.", JsonSerializer.Serialize(CredentialCheck.Refused(Check.Wrong, "Wrong password.").Value));
    }
}
