using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace api.Tests;

// re-authentication, authenticator-app two-factor, email change and the activity log
public class SecurityTests
{
    const string Pass = "Passw0rd1!";
    const string NewPass = "An0ther!pass";

    // records the mail EmailSender posts to Resend
    class Outbox : HttpMessageHandler
    {
        public List<(string To, string Text)> Sent = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            Sent.Add((json.GetProperty("to")[0].GetString()!, json.GetProperty("text").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    sealed class Rig : IDisposable
    {
        public readonly ServiceProvider P;
        public readonly Outbox Mail = new();
        public readonly UserManager<AppUser> Users;
        public readonly SecurityLog Events;
        public readonly SessionService Sessions;
        readonly CredentialCheck _check;
        readonly EmailSender _email;
        readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        readonly IConfiguration _config;

        public Rig()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddAuthentication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
            P = services.BuildServiceProvider();

            _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = new string('k', 40), ["Jwt:Issuer"] = "test",
                ["App:FrontendUrl"] = "http://app", ["Resend:ApiKey"] = "re_test"
            }).Build();
            var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" };
            var db = P.GetRequiredService<AppDbContext>();
            Users = P.GetRequiredService<UserManager<AppUser>>();
            Events = new SecurityLog(db);
            Sessions = new SessionService(db, Users, _config, env);
            _check = new CredentialCheck(Users, Events);
            _email = new EmailSender(_config, NullLogger<EmailSender>.Instance, env, new HttpClient(Mail));
        }

        public void Dispose() => P.Dispose();

        public async Task<AppUser> AddUser(string name)
        {
            var user = new AppUser { UserName = name, Email = name + "@example.com" };
            Assert.True((await Users.CreateAsync(user, Pass)).Succeeded);
            return user;
        }

        public AuthController Auth(HttpContext? http = null) =>
            new(Users, _email, Sessions, _check, Events, _config, NullLogger<AuthController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = http ?? new DefaultHttpContext() } };

        public AccountController Account(AppUser user, Guid sid) =>
            new(Users, _email, Sessions, _check, Events, _cache, NullLogger<AccountController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = Signed(user, sid) } };

        public TwoFactorController TwoFactor(AppUser user, Guid sid) =>
            new(Users, Sessions, _check, Events) { ControllerContext = new ControllerContext { HttpContext = Signed(user, sid) } };

        static HttpContext Signed(AppUser user, Guid sid) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id), new Claim("sid", sid.ToString()) }, "test"))
        };
    }

    static JsonElement Body(ObjectResult r) => JsonSerializer.SerializeToElement(r.Value);
    static string Message(ObjectResult r) => Body(r).GetProperty("message").GetString()!;
    static T Ok<T>(IActionResult r) where T : class => Assert.IsType<T>(r);
    static int Status(IActionResult r) => Assert.IsType<ObjectResult>(r).StatusCode!.Value;

    // RFC 6238: 6 digits, 30 second steps, HMAC-SHA1; the key is the base32 text Identity hands out
    static string Totp(string key)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in key.ToUpperInvariant())
        {
            buffer = (buffer << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
        }
        var step = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        Array.Reverse(step);
        var hash = HMACSHA1.HashData(bytes.ToArray(), step);
        var o = hash[^1] & 0xf;
        var n = ((hash[o] & 0x7f) << 24) | (hash[o + 1] << 16) | (hash[o + 2] << 8) | hash[o + 3];
        return (n % 1_000_000).ToString("D6");
    }

    // sets up and turns on 2FA for a user; returns the key and the recovery codes
    static async Task<(string Key, string[] Codes)> EnableTwoFactor(Rig r, AppUser user, Guid sid)
    {
        var tf = r.TwoFactor(user, sid);
        var key = Body(Ok<OkObjectResult>(await tf.Setup())).GetProperty("key").GetString()!;
        var res = Body(Ok<OkObjectResult>(await tf.Enable(new CodeRequest(Totp(key)))));
        return (key, res.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray());
    }

    static async Task<string> Challenge(Rig r, string email)
    {
        var res = Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest(email, Pass)));
        return Body(res).GetProperty("challengeToken").GetString()!;
    }

    [Fact]
    public async Task Password_change_needs_the_current_password_and_ends_the_other_sessions()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (phone, phoneToken) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await r.Sessions.Create(user, "laptop", "2.2.2.2");
        var account = r.Account(user, phone.Id);

        var wrong = Assert.IsType<ObjectResult>(await account.ChangePassword(new ChangePasswordRequest("nope", NewPass)));
        Assert.Equal(403, wrong.StatusCode); // not 401: the web client would retry a 401
        Assert.Equal("Wrong password.", Message(wrong));
        Assert.IsType<BadRequestObjectResult>(await account.ChangePassword(new ChangePasswordRequest(Pass, "short")));
        Assert.Equal(2, (await r.Sessions.Active(user.Id)).Count);

        Ok<OkObjectResult>(await account.ChangePassword(new ChangePasswordRequest(Pass, NewPass)));

        Assert.Equal(new[] { phone.Id }, (await r.Sessions.Active(user.Id)).Select(s => s.Id).ToArray());
        Assert.NotNull(await r.Sessions.Refresh(user.Id, phoneToken)); // the caller stays signed in
        Assert.True(await r.Users.CheckPasswordAsync(user, NewPass));
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "password-changed");
    }

    [Fact]
    public async Task Wrong_current_passwords_count_toward_lockout_and_log_it_once()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var account = r.Account(user, session.Id);
        var bad = new ChangePasswordRequest("nope", NewPass);

        for (var i = 1; i <= 4; i++) Assert.Equal(403, Status(await account.ChangePassword(bad)));
        Assert.Equal(423, Status(await account.ChangePassword(bad))); // the 5th miss locks
        Assert.Equal(423, Status(await account.ChangePassword(new ChangePasswordRequest(Pass, NewPass)))); // even the right one
        Assert.Single((await r.Events.Latest(user.Id)).Where(e => e.Type == "locked-out"));
    }

    [Fact]
    public async Task Setup_gives_the_key_and_link_and_enable_needs_a_valid_code()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (phone, phoneToken) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await r.Sessions.Create(user, "laptop", "2.2.2.2");
        var tf = r.TwoFactor(user, phone.Id);

        var setup = Body(Ok<OkObjectResult>(await tf.Setup()));
        var key = setup.GetProperty("key").GetString()!;
        Assert.Equal($"otpauth://totp/Anime%20Rating%20Platform:kai%40example.com?secret={key}&issuer=Anime%20Rating%20Platform&digits=6",
            setup.GetProperty("uri").GetString());
        Assert.False(Body(Ok<OkObjectResult>(await tf.Status())).GetProperty("enabled").GetBoolean());

        Assert.IsType<BadRequestObjectResult>(await tf.Enable(new CodeRequest("000000")));
        Assert.False(user.TwoFactorEnabled);

        var codes = Body(Ok<OkObjectResult>(await tf.Enable(new CodeRequest(Totp(key))))).GetProperty("recoveryCodes");
        Assert.Equal(10, codes.GetArrayLength());
        var status = Body(Ok<OkObjectResult>(await tf.Status()));
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.IsType<BadRequestObjectResult>(await tf.Setup()); // already on
        // other devices end (the stamp changed), this one stays
        Assert.Equal(new[] { phone.Id }, (await r.Sessions.Active(user.Id)).Select(s => s.Id).ToArray());
        Assert.NotNull(await r.Sessions.Refresh(user.Id, phoneToken));
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "2fa-enabled");
    }

    [Fact]
    public async Task Login_with_2fa_gives_a_challenge_and_no_session_until_the_code_passes()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (setupSession, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (key, _) = await EnableTwoFactor(r, user, setupSession.Id);
        var before = (await r.Sessions.Active(user.Id)).Count;

        var http = new DefaultHttpContext();
        var login = Body(Ok<OkObjectResult>(await r.Auth(http).Login(new LoginRequest("kai@example.com", Pass))));
        Assert.True(login.GetProperty("twoFactorRequired").GetBoolean());
        Assert.False(login.TryGetProperty("accessToken", out _));
        Assert.Equal("", http.Response.Headers.SetCookie.ToString());
        Assert.Equal(before, (await r.Sessions.Active(user.Id)).Count);
        var challenge = login.GetProperty("challengeToken").GetString()!;

        var codeHttp = new DefaultHttpContext();
        var done = Body(Ok<OkObjectResult>(await r.Auth(codeHttp).TwoFactor(new TwoFactorLoginRequest(challenge, Totp(key)))));
        Assert.False(string.IsNullOrEmpty(done.GetProperty("accessToken").GetString()));
        Assert.StartsWith(SessionService.CookieName(user.Id).ToLowerInvariant() + "=", codeHttp.Response.Headers.SetCookie.ToString().ToLowerInvariant());
        Assert.Equal(before + 1, (await r.Sessions.Active(user.Id)).Count);
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "login");
    }

    [Fact]
    public async Task Wrong_codes_count_toward_lockout_and_a_new_password_login_does_not_reset_them()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (key, _) = await EnableTwoFactor(r, user, session.Id);
        var challenge = await Challenge(r, "kai@example.com");
        var bad = new TwoFactorLoginRequest(challenge, "000000");

        for (var i = 1; i <= 3; i++)
            Assert.Equal("Invalid code.", Message(Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(bad))));
        var again = await Challenge(r, "kai@example.com"); // logging in again must not wipe the misses
        bad = new TwoFactorLoginRequest(again, "000000");
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(bad));
        Assert.Equal(423, Status(await r.Auth().TwoFactor(bad))); // the 5th miss

        Assert.Equal(423, Status(await r.Auth().TwoFactor(new TwoFactorLoginRequest(again, Totp(key))))); // even the right code
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "locked-out");
    }

    [Fact]
    public async Task A_recovery_code_works_once_and_an_empty_code_never()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (_, codes) = await EnableTwoFactor(r, user, session.Id);
        var first = await Challenge(r, "kai@example.com");
        Ok<OkObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(first, codes[0])));
        Assert.Equal(9, Body(Ok<OkObjectResult>(await r.TwoFactor(user, session.Id).Status())).GetProperty("recoveryCodesLeft").GetInt32());
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(first, codes[0]))); // spent

        // no codes left: "" must not match the empty list
        await r.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(first, "")));
    }

    [Fact]
    public async Task The_challenge_token_is_not_an_access_token_and_lasts_five_minutes()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var challenge = r.Sessions.ChallengeToken(user);
        var access = await r.Sessions.AccessToken(user, session.Id);

        Assert.Equal(user.Id, r.Sessions.ReadChallenge(challenge));
        Assert.Null(r.Sessions.ReadChallenge(access)); // an access token is no challenge either
        Assert.Null(r.Sessions.ReadChallenge("garbage"));
        Assert.Null(r.Sessions.ReadChallenge(null));
        Assert.InRange(new JwtSecurityTokenHandler().ReadJwtToken(challenge).ValidTo, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));

        // the validation parameters of Program.cs's JwtBearer
        var asAccessToken = new TokenValidationParameters
        {
            ValidIssuer = "test", ValidAudience = "test",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 40)))
        };
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => new JwtSecurityTokenHandler().ValidateToken(challenge, asAccessToken, out _));
        new JwtSecurityTokenHandler().ValidateToken(access, asAccessToken, out _); // the check itself works
    }

    [Fact]
    public async Task A_bad_challenge_or_a_user_without_2fa_gets_nothing()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest("garbage", "123456")));
        // a valid challenge for an account that has no 2FA
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(r.Sessions.ChallengeToken(user), "123456")));
    }

    [Fact]
    public async Task Disable_needs_the_password_and_a_code_and_then_login_is_plain_again()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (key, codes) = await EnableTwoFactor(r, user, session.Id);
        var tf = r.TwoFactor(user, session.Id);

        Assert.Equal(403, Status(await tf.Disable(new DisableTwoFactorRequest("nope", Totp(key)))));
        var badCode = Assert.IsType<ObjectResult>(await tf.Disable(new DisableTwoFactorRequest(Pass, "000000")));
        Assert.Equal(403, badCode.StatusCode);
        Assert.Equal("Invalid code.", Message(badCode));
        Assert.True(user.TwoFactorEnabled);

        Assert.IsType<NoContentResult>(await tf.Disable(new DisableTwoFactorRequest(Pass, codes[0]))); // a recovery code is fine too
        Assert.False(user.TwoFactorEnabled);
        var login = Body(Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", Pass))));
        Assert.False(string.IsNullOrEmpty(login.GetProperty("accessToken").GetString()));
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "2fa-disabled");
    }

    [Fact]
    public async Task New_recovery_codes_need_the_password_and_replace_the_old_ones()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (_, old) = await EnableTwoFactor(r, user, session.Id);
        var tf = r.TwoFactor(user, session.Id);

        Assert.Equal(403, Status(await tf.NewRecoveryCodes(new PasswordRequest("nope"))));
        var fresh = Body(Ok<OkObjectResult>(await tf.NewRecoveryCodes(new PasswordRequest(Pass)))).GetProperty("recoveryCodes");
        Assert.Equal(10, fresh.GetArrayLength());

        var challenge = await Challenge(r, "kai@example.com");
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, old[0])));
        Ok<OkObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, fresh[0].GetString()!)));
        Assert.Contains(await r.Events.Latest(user.Id), e => e.Type == "recovery-codes-regenerated");
    }

    [Fact]
    public async Task Email_change_mails_the_new_address_and_only_the_link_applies_it()
    {
        using var r = new Rig();
        var kai = await r.AddUser("kai");
        await r.AddUser("ann");
        var (phone, _) = await r.Sessions.Create(kai, "phone", "1.1.1.1");
        await r.Sessions.Create(kai, "laptop", "2.2.2.2");
        var account = r.Account(kai, phone.Id);

        Assert.Equal(403, Status(await account.ChangeEmail(new ChangeEmailRequest("new@example.com", "nope"))));
        Assert.Equal("That email is already in use.", Message(Assert.IsType<BadRequestObjectResult>(await account.ChangeEmail(new ChangeEmailRequest("ann@example.com", Pass)))));
        Assert.IsType<BadRequestObjectResult>(await account.ChangeEmail(new ChangeEmailRequest("not an email", Pass)));
        Assert.Empty(r.Mail.Sent);

        Ok<OkObjectResult>(await account.ChangeEmail(new ChangeEmailRequest("new@example.com", Pass)));

        Assert.Equal("kai@example.com", kai.Email); // nothing changes yet
        Assert.Equal(2, r.Mail.Sent.Count);
        var confirm = r.Mail.Sent.Single(m => m.To == "new@example.com").Text;
        Assert.Contains("someone requested", r.Mail.Sent.Single(m => m.To == "kai@example.com").Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new@example.com", r.Mail.Sent.Single(m => m.To == "kai@example.com").Text);
        var query = QueryHelpers.ParseQuery(new Uri(confirm[confirm.IndexOf("http")..]).Query);
        Assert.StartsWith("http://app/confirm-email-change?", confirm[confirm.IndexOf("http")..]);
        string Q(string k) => query[k].ToString();
        Assert.Equal(429, Status(await account.ChangeEmail(new ChangeEmailRequest("other@example.com", Pass)))); // cooldown

        // the token only fits the address it was made for
        Assert.IsType<BadRequestObjectResult>(await r.Auth().ConfirmEmailChange(new ConfirmEmailChangeRequest(Q("userId"), "other@example.com", Q("token"))));
        Assert.Equal("kai@example.com", kai.Email);

        Ok<OkObjectResult>(await r.Auth().ConfirmEmailChange(new ConfirmEmailChangeRequest(Q("userId"), Q("newEmail"), Q("token"))));
        Assert.Equal("new@example.com", kai.Email);
        Assert.True(kai.EmailConfirmed);
        Assert.Equal("kai", kai.UserName);
        Assert.Empty(await r.Sessions.Active(kai.Id));
        var types = (await r.Events.Latest(kai.Id)).Select(e => e.Type).ToArray();
        Assert.Contains("email-change-requested", types);
        Assert.Contains("email-changed", types);
        // the link works once
        Assert.IsType<BadRequestObjectResult>(await r.Auth().ConfirmEmailChange(new ConfirmEmailChangeRequest(Q("userId"), Q("newEmail"), Q("token"))));
    }

    [Fact]
    public async Task Activity_lists_only_the_callers_latest_50_events()
    {
        using var r = new Rig();
        var kai = await r.AddUser("kai");
        var ann = await r.AddUser("ann");
        Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", Pass)));
        Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("ann@example.com", Pass)));
        var (session, _) = await r.Sessions.Create(kai, "phone", "1.1.1.1");

        async Task<JsonElement> List() =>
            JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<IEnumerable<object>>(Ok<OkObjectResult>(await r.Account(kai, session.Id).Activity()).Value));

        var mine = await List();
        Assert.Equal(1, mine.GetArrayLength());
        Assert.Equal("login", mine[0].GetProperty("Type").GetString());

        for (var i = 0; i < 60; i++) await r.Events.Add(kai.Id, "session-revoked", new DefaultHttpContext());
        var latest = await List();
        Assert.Equal(50, latest.GetArrayLength());
        Assert.Equal("session-revoked", latest[0].GetProperty("Type").GetString()); // newest first
        Assert.Single(await r.Events.Latest(ann.Id));
    }

    [Fact]
    public async Task Logout_writes_an_event_only_for_a_real_session_cookie()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var login = new DefaultHttpContext();
        await r.Auth(login).Login(new LoginRequest("kai@example.com", Pass));
        var cookie = login.Response.Headers.SetCookie.ToString().Split('=', 2)[1].Split(';')[0];

        HttpContext With(string token)
        {
            var http = new DefaultHttpContext();
            http.Request.Headers.Cookie = $"{SessionService.CookieName(user.Id)}={token}";
            return http;
        }

        await r.Auth(With("bogus")).Logout(new UserIdRequest(user.Id));
        Assert.DoesNotContain(await r.Events.Latest(user.Id), e => e.Type == "logout");
        await r.Auth(With(cookie)).Logout(new UserIdRequest(user.Id));
        Assert.Single((await r.Events.Latest(user.Id)).Where(e => e.Type == "logout"));
    }

    [Fact]
    public async Task User_agent_in_an_event_is_trimmed_to_200_characters()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var http = new DefaultHttpContext();
        http.Request.Headers.UserAgent = new string('a', 500);
        await r.Events.Add(user.Id, "login", http);
        Assert.Equal(200, (await r.Events.Latest(user.Id)).Single().UserAgent.Length);
    }

    [Fact]
    public async Task Session_revoke_endpoints_write_events()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (phone, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var (laptop, _) = await r.Sessions.Create(user, "laptop", "2.2.2.2");
        var api = new SessionsController(r.Sessions, r.Events)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id), new Claim("sid", phone.Id.ToString()) }, "test"))
                }
            }
        };

        await api.Revoke(laptop.Id);
        await api.RevokeOthers();

        var types = (await r.Events.Latest(user.Id)).Select(e => e.Type).ToArray();
        Assert.Contains("session-revoked", types);
        Assert.Contains("other-sessions-revoked", types);
    }
}
