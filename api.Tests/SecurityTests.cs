using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

// re-authentication, emailed-code two-step sign-in, email change and display name
public class SecurityTests
{
    const string Pass = "correct horse battery"; // the real policy (12+ characters) applies
    const string NewPass = "a brand new passphrase";

    // records the mail EmailSender posts to Resend
    class Outbox : HttpMessageHandler
    {
        public List<(string To, string Subject, string Text)> Sent = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            Sent.Add((json.GetProperty("to")[0].GetString()!, json.GetProperty("subject").GetString()!, json.GetProperty("text").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    sealed class Rig : IDisposable
    {
        public readonly ServiceProvider P;
        public readonly Outbox Mail = new();
        public readonly UserManager<AppUser> Users;
        public readonly SessionService Sessions;
        public readonly IMemoryCache Cache = new MemoryCache(new MemoryCacheOptions());
        readonly CredentialCheck _check;
        readonly EmailSender _email;
        readonly IConfiguration _config;

        public Rig()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddAuthentication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentityCore<AppUser>(IdentitySetup.Configure).AddRoles<IdentityRole>()
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
            Sessions = new SessionService(db, Users, _config, env);
            _check = new CredentialCheck(Users);
            _email = new EmailSender(_config, NullLogger<EmailSender>.Instance, env, new HttpClient(Mail));
        }

        public void Dispose() => P.Dispose();

        public async Task<AppUser> AddUser(string name, bool confirmed = true)
        {
            var user = new AppUser { UserName = name, Email = name + "@example.com", EmailConfirmed = confirmed };
            Assert.True((await Users.CreateAsync(user, Pass)).Succeeded);
            return user;
        }

        public AuthController Auth(HttpContext? http = null) =>
            new(Users, _email, Sessions, _check, Cache, _config, NullLogger<AuthController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = http ?? new DefaultHttpContext() } };

        public AccountController Account(AppUser user, Guid sid) =>
            new(Users, _email, Sessions, _check, Cache, NullLogger<AccountController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = Signed(user, sid) } };

        public TwoFactorController TwoFactor(AppUser user, Guid sid) =>
            new(Users, Sessions, _check) { ControllerContext = new ControllerContext { HttpContext = Signed(user, sid) } };

        // the code in the newest "Your sign-in code" mail
        public string LastCode()
        {
            var mail = Mail.Sent.Last(m => m.Subject == "Your sign-in code");
            return Regex.Match(mail.Text, "[0-9]{6}").Value;
        }

        // pretends 30 seconds passed, so the next send is allowed
        public void SkipCooldown(AppUser user) => Cache.Remove("2fa-code:" + user.Id);

        static HttpContext Signed(AppUser user, Guid sid) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id), new Claim("sid", sid.ToString()) }, "test"))
        };
    }

    static JsonElement Body(ObjectResult r) => JsonSerializer.SerializeToElement(r.Value);
    static string Message(ObjectResult r) => Body(r).GetProperty("message").GetString()!;
    static T Ok<T>(IActionResult r) where T : class => Assert.IsType<T>(r);
    static int Status(IActionResult r) => Assert.IsType<ObjectResult>(r).StatusCode!.Value;

    // turns two-step verification on for a user
    static async Task EnableTwoFactor(Rig r, AppUser user, Guid sid) =>
        Assert.IsType<NoContentResult>(await r.TwoFactor(user, sid).Enable(new PasswordRequest(Pass)));

    // password login of a user who has two-step on; the answer holds the challenge and a code was mailed
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
    }

    [Fact]
    public async Task Wrong_current_passwords_count_toward_lockout()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var account = r.Account(user, session.Id);
        var bad = new ChangePasswordRequest("nope", NewPass);

        for (var i = 1; i <= 9; i++) Assert.Equal(403, Status(await account.ChangePassword(bad)));
        Assert.Equal(423, Status(await account.ChangePassword(bad))); // the 10th miss locks
        Assert.Equal(423, Status(await account.ChangePassword(new ChangePasswordRequest(Pass, NewPass)))); // even the right one
    }

    [Fact]
    public async Task Enable_needs_the_password_and_a_confirmed_email_and_ends_the_other_sessions()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (phone, phoneToken) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await r.Sessions.Create(user, "laptop", "2.2.2.2");
        var tf = r.TwoFactor(user, phone.Id);

        Assert.False(Body(Ok<OkObjectResult>(await tf.Status())).GetProperty("enabled").GetBoolean());
        Assert.Equal(403, Status(await tf.Enable(new PasswordRequest("nope"))));
        Assert.False(user.TwoFactorEnabled);

        Assert.IsType<NoContentResult>(await tf.Enable(new PasswordRequest(Pass)));
        Assert.True(Body(Ok<OkObjectResult>(await tf.Status())).GetProperty("enabled").GetBoolean());
        Assert.IsType<BadRequestObjectResult>(await tf.Enable(new PasswordRequest(Pass))); // already on
        // other devices end (the stamp changed), this one stays
        Assert.Equal(new[] { phone.Id }, (await r.Sessions.Active(user.Id)).Select(s => s.Id).ToArray());
        Assert.NotNull(await r.Sessions.Refresh(user.Id, phoneToken));

        // the codes go to the account address, so it must be confirmed
        var fresh = await r.AddUser("ann", confirmed: false);
        var (annSession, _) = await r.Sessions.Create(fresh, "phone", "3.3.3.3");
        Assert.IsType<BadRequestObjectResult>(await r.TwoFactor(fresh, annSession.Id).Enable(new PasswordRequest(Pass)));
        Assert.False(fresh.TwoFactorEnabled);
    }

    [Fact]
    public async Task A_google_only_account_gets_the_no_password_answer_from_enable()
    {
        using var r = new Rig();
        var user = new AppUser { UserName = "gina", Email = "gina@example.com", EmailConfirmed = true };
        Assert.True((await r.Users.CreateAsync(user)).Succeeded);
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");

        var res = Assert.IsType<ObjectResult>(await r.TwoFactor(user, session.Id).Enable(new PasswordRequest(Pass)));
        Assert.Equal(403, res.StatusCode);
        Assert.Contains("no password", Message(res));
        Assert.False(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task Login_with_2fa_emails_a_code_and_gives_no_session_until_it_passes()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (setupSession, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, setupSession.Id);
        var before = (await r.Sessions.Active(user.Id)).Count;

        var http = new DefaultHttpContext();
        var login = Body(Ok<OkObjectResult>(await r.Auth(http).Login(new LoginRequest("kai@example.com", Pass))));
        Assert.True(login.GetProperty("twoFactorRequired").GetBoolean());
        Assert.False(login.TryGetProperty("accessToken", out _));
        Assert.Equal("", http.Response.Headers.SetCookie.ToString());
        Assert.Equal(before, (await r.Sessions.Active(user.Id)).Count);
        var challenge = login.GetProperty("challengeToken").GetString()!;

        var mail = r.Mail.Sent.Single(m => m.Subject == "Your sign-in code");
        Assert.Equal("kai@example.com", mail.To);
        Assert.Contains("expires", mail.Text);
        Assert.DoesNotContain(r.LastCode(), challenge); // the code is not inside the token

        var codeHttp = new DefaultHttpContext();
        var done = Body(Ok<OkObjectResult>(await r.Auth(codeHttp).TwoFactor(new TwoFactorLoginRequest(challenge, r.LastCode()))));
        Assert.False(string.IsNullOrEmpty(done.GetProperty("accessToken").GetString()));
        Assert.StartsWith(SessionService.CookieName(user.Id).ToLowerInvariant() + "=", codeHttp.Response.Headers.SetCookie.ToString().ToLowerInvariant());
        Assert.Equal(before + 1, (await r.Sessions.Active(user.Id)).Count);
    }

    [Fact]
    public async Task A_wrong_password_sends_no_code_and_a_user_without_2fa_gets_a_session_at_once()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, session.Id);

        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", "nope")));
        Assert.Empty(r.Mail.Sent);

        await r.AddUser("ann");
        var plain = Body(Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("ann@example.com", Pass))));
        Assert.False(string.IsNullOrEmpty(plain.GetProperty("accessToken").GetString()));
        Assert.Empty(r.Mail.Sent);
    }

    [Fact]
    public async Task Wrong_codes_count_toward_lockout_and_a_new_password_login_does_not_reset_them()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, session.Id);
        var challenge = await Challenge(r, "kai@example.com");
        var good = r.LastCode();
        var wrong = good == "000000" ? "111111" : "000000";
        var bad = new TwoFactorLoginRequest(challenge, wrong);

        for (var i = 1; i <= 3; i++)
            Assert.Equal("Invalid code.", Message(Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(bad))));
        var again = await Challenge(r, "kai@example.com"); // logging in again must not wipe the misses
        bad = new TwoFactorLoginRequest(again, wrong);
        for (var i = 4; i <= 9; i++) Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(bad));
        Assert.Equal(423, Status(await r.Auth().TwoFactor(bad))); // the 10th miss

        Assert.Equal(423, Status(await r.Auth().TwoFactor(new TwoFactorLoginRequest(again, good)))); // even the right code
    }

    [Fact]
    public async Task An_empty_code_never_passes_and_spaces_in_the_code_are_ignored()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, session.Id);
        var challenge = await Challenge(r, "kai@example.com");

        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, "")));
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, "   ")));
        var code = r.LastCode();
        Ok<OkObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, code[..3] + " " + code[3..])));
    }

    [Fact]
    public async Task Resend_sends_a_new_mail_at_most_every_30_seconds_with_the_same_answer()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, session.Id);
        var challenge = await Challenge(r, "kai@example.com");
        Assert.Single(r.Mail.Sent); // the login sent the first one

        var tooSoon = Ok<OkObjectResult>(await r.Auth().ResendCode(new ChallengeRequest(challenge)));
        Assert.Single(r.Mail.Sent);

        r.SkipCooldown(user);
        var later = Ok<OkObjectResult>(await r.Auth().ResendCode(new ChallengeRequest(challenge)));
        Assert.Equal(2, r.Mail.Sent.Count);
        Assert.Equal(JsonSerializer.Serialize(tooSoon.Value), JsonSerializer.Serialize(later.Value)); // generic answer

        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().ResendCode(new ChallengeRequest("garbage")));
        Assert.Equal(2, r.Mail.Sent.Count);

        // a locked account gets no more mail
        await r.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
        r.SkipCooldown(user);
        Assert.Equal(423, Status(await r.Auth().ResendCode(new ChallengeRequest(challenge))));
        Assert.Equal(2, r.Mail.Sent.Count);
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
        var challenge = r.Sessions.ChallengeToken(user);
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().TwoFactor(new TwoFactorLoginRequest(challenge, "123456")));
        Assert.IsType<UnauthorizedObjectResult>(await r.Auth().ResendCode(new ChallengeRequest(challenge)));
        Assert.Empty(r.Mail.Sent);
    }

    [Fact]
    public async Task Disable_needs_the_password_and_then_login_is_plain_again()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        await EnableTwoFactor(r, user, session.Id);
        var tf = r.TwoFactor(user, session.Id);

        Assert.Equal(403, Status(await tf.Disable(new PasswordRequest("nope"))));
        Assert.True(user.TwoFactorEnabled);

        Assert.IsType<NoContentResult>(await tf.Disable(new PasswordRequest(Pass)));
        Assert.False(user.TwoFactorEnabled);
        Assert.IsType<BadRequestObjectResult>(await tf.Disable(new PasswordRequest(Pass))); // already off
        var login = Body(Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", Pass))));
        Assert.False(string.IsNullOrEmpty(login.GetProperty("accessToken").GetString()));
        Assert.Empty(r.Mail.Sent);
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
        // the link works once
        Assert.IsType<BadRequestObjectResult>(await r.Auth().ConfirmEmailChange(new ConfirmEmailChangeRequest(Q("userId"), Q("newEmail"), Q("token"))));
    }

    [Fact]
    public async Task Forgot_password_and_resend_confirmation_mail_once_a_minute_per_address_with_the_same_answer()
    {
        using var r = new Rig();
        await r.AddUser("kai", confirmed: false);

        var first = Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("kai@example.com")));
        var again = Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("KAI@example.com"))); // same address, any case
        Assert.Single(r.Mail.Sent);
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(again.Value));
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("nobody@example.com"))).Value));

        Ok<OkObjectResult>(await r.Auth().Resend(new EmailRequest("kai@example.com")));
        Ok<OkObjectResult>(await r.Auth().Resend(new EmailRequest("kai@example.com")));
        Assert.Equal(2, r.Mail.Sent.Count); // one reset, one confirmation

        r.Cache.Remove("forgot-password:kai@example.com"); // a minute later
        Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("kai@example.com")));
        Assert.Equal(3, r.Mail.Sent.Count);
    }

    [Fact]
    public async Task Email_change_mails_one_target_address_once_a_minute_even_from_different_accounts()
    {
        using var r = new Rig();
        var kai = await r.AddUser("kai");
        var ann = await r.AddUser("ann");
        var (kaiSession, _) = await r.Sessions.Create(kai, "phone", "1.1.1.1");
        var (annSession, _) = await r.Sessions.Create(ann, "phone", "2.2.2.2");

        var first = Ok<OkObjectResult>(await r.Account(kai, kaiSession.Id).ChangeEmail(new ChangeEmailRequest("target@example.com", Pass)));
        Assert.Equal(2, r.Mail.Sent.Count); // the link and the notice to the old address
        var second = Ok<OkObjectResult>(await r.Account(ann, annSession.Id).ChangeEmail(new ChangeEmailRequest("target@example.com", Pass)));

        Assert.Equal(2, r.Mail.Sent.Count);
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(second.Value));
    }

    [Fact]
    public async Task Display_name_change_checks_the_format_and_uniqueness_and_keeps_other_devices_signed_in()
    {
        using var r = new Rig();
        var kai = await r.AddUser("kai");
        await r.AddUser("ann");
        var (phone, phoneToken) = await r.Sessions.Create(kai, "phone", "1.1.1.1");
        await r.Sessions.Create(kai, "laptop", "2.2.2.2");
        var account = r.Account(kai, phone.Id);

        foreach (var bad in new[] { "ab", new string('a', 31), "has space", "a@b.com", "tab\there", "", "naïve" })
            Assert.IsType<BadRequestObjectResult>(await account.ChangeUserName(new ChangeUserNameRequest(bad)));
        Assert.Equal("That display name is not available.", Message(Assert.IsType<BadRequestObjectResult>(await account.ChangeUserName(new ChangeUserNameRequest("Ann")))));
        Assert.NotNull(await r.Users.FindByNameAsync("kai")); // nothing was saved

        var ok = Body(Ok<OkObjectResult>(await account.ChangeUserName(new ChangeUserNameRequest("  Kai_2.0-x  "))));
        Assert.Equal("Kai_2.0-x", ok.GetProperty("userName").GetString());
        Assert.Equal("Kai_2.0-x", (await r.Users.FindByNameAsync("kai_2.0-x"))!.UserName);
        Assert.Null(await r.Users.FindByNameAsync("kai"));

        // no password asked, and no device signed out
        Assert.Equal(2, (await r.Sessions.Active(kai.Id)).Count);
        Assert.NotNull(await r.Sessions.Refresh(kai.Id, phoneToken));
        Ok<OkObjectResult>(await account.ChangeUserName(new ChangeUserNameRequest("Kai_2.0-x"))); // same name again is fine
    }

    [Fact]
    public async Task Logout_ends_the_session_only_for_a_real_session_cookie()
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
        Assert.Single(await r.Sessions.Active(user.Id));
        await r.Auth(With(cookie)).Logout(new UserIdRequest(user.Id));
        Assert.Empty(await r.Sessions.Active(user.Id));
    }
}
