using System.Net;
using System.Security.Claims;
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

namespace api.Tests;

// re-authentication, emailed codes (verify the email, forgot password), email change and display name
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
                ["App:FrontendUrl"] = "http://app", ["Resend:ApiKey"] = "re_test", ["Admin:Email"] = "boss@example.com"
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
            new(Users, _email, Sessions, _check, Cache, _config, NullLogger<AccountController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = Signed(user, sid) } };

        public RatingsController Ratings(AppUser user) =>
            new(P.GetRequiredService<AppDbContext>()) { ControllerContext = new ControllerContext { HttpContext = Signed(user, Guid.NewGuid()) } };

        // the code in the newest mail with that subject
        public string LastCode(string subject)
        {
            var mail = Mail.Sent.Last(m => m.Subject == subject);
            return Regex.Match(mail.Text, "[0-9]{6}").Value;
        }

        // a code made without sending a mail
        public Task<string> MakeCode(AppUser user, string purpose) =>
            Users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, purpose);

        static HttpContext Signed(AppUser user, Guid sid) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id), new Claim("sid", sid.ToString()) }, "test"))
        };
    }

    static JsonElement Body(ObjectResult r) => JsonSerializer.SerializeToElement(r.Value);
    static string Message(ObjectResult r) => Body(r).GetProperty("message").GetString()!;
    static T Ok<T>(IActionResult r) where T : class => Assert.IsType<T>(r);
    static int Status(IActionResult r) => Assert.IsType<ObjectResult>(r).StatusCode!.Value;
    static string Wrong(string good) => good == "000000" ? "111111" : "000000";

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
    public async Task Login_never_asks_for_a_code_even_for_an_account_that_had_two_step_on()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        await r.Users.SetTwoFactorEnabledAsync(user, true); // as before the switch-off

        var login = Body(Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", Pass))));

        Assert.False(string.IsNullOrEmpty(login.GetProperty("accessToken").GetString()));
        Assert.False(login.TryGetProperty("twoFactorRequired", out _));
        Assert.Empty(r.Mail.Sent);
    }

    [Fact]
    public async Task Register_sends_no_mail_and_the_account_starts_unverified()
    {
        using var r = new Rig();

        Ok<OkObjectResult>(await r.Auth().Register(new RegisterRequest("new@example.com", Pass, null)));

        Assert.Empty(r.Mail.Sent);
        Assert.False((await r.Users.FindByEmailAsync("new@example.com"))!.EmailConfirmed);
    }

    [Fact]
    public async Task Verify_send_mails_a_code_at_most_every_30_seconds_and_skips_verified_users()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai", confirmed: false);
        var verified = await r.AddUser("ann");
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");

        Ok<OkObjectResult>(await r.Account(user, session.Id).SendVerifyCode());
        var mail = r.Mail.Sent.Single();
        Assert.Equal("kai@example.com", mail.To);
        Assert.Equal("Your verification code", mail.Subject);
        Assert.Matches("^[0-9]{6}$", r.LastCode("Your verification code"));

        Assert.Equal(429, Status(await r.Account(user, session.Id).SendVerifyCode())); // too soon
        Assert.Single(r.Mail.Sent);

        r.Cache.Remove("verify-code:" + user.Id); // 30 seconds later
        Ok<OkObjectResult>(await r.Account(user, session.Id).SendVerifyCode());
        Assert.Equal(2, r.Mail.Sent.Count);

        var (annSession, _) = await r.Sessions.Create(verified, "phone", "2.2.2.2");
        var already = Body(Ok<OkObjectResult>(await r.Account(verified, annSession.Id).SendVerifyCode()));
        Assert.True(already.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(2, r.Mail.Sent.Count); // nothing sent
    }

    [Fact]
    public async Task Verify_needs_the_right_code_then_confirms_the_email_and_promotes_the_admin()
    {
        using var r = new Rig();
        await r.P.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole("Admin"));
        var boss = await r.AddUser("boss", confirmed: false); // the configured admin address
        var (session, _) = await r.Sessions.Create(boss, "phone", "1.1.1.1");
        var account = r.Account(boss, session.Id);
        await account.SendVerifyCode();
        var good = r.LastCode("Your verification code");

        var wrong = Assert.IsType<BadRequestObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(Wrong(good)))); // 400, not 401
        Assert.Equal("Wrong or expired code.", Message(wrong));
        Assert.IsType<BadRequestObjectResult>(await account.VerifyEmail(new VerifyEmailRequest("")));
        Assert.IsType<BadRequestObjectResult>(await account.VerifyEmail(new VerifyEmailRequest("   ")));
        // a code made for another purpose does not verify
        Assert.IsType<BadRequestObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(await r.MakeCode(boss, EmailCodes.Reset))));
        Assert.False((await r.Users.FindByIdAsync(boss.Id))!.EmailConfirmed);
        Assert.False(await r.Users.IsInRoleAsync(boss, "Admin"));

        var done = Body(Ok<OkObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(good[..3] + " " + good[3..]))));
        Assert.True(done.GetProperty("emailVerified").GetBoolean());
        Assert.True((await r.Users.FindByIdAsync(boss.Id))!.EmailConfirmed);
        Assert.True(await r.Users.IsInRoleAsync(boss, "Admin"));
        Assert.Single(await r.Sessions.Active(boss.Id)); // verifying signs nobody out
    }

    [Fact]
    public async Task Verify_stops_after_5_tries_and_a_right_code_gives_them_back()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai", confirmed: false);
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var account = r.Account(user, session.Id);
        await account.SendVerifyCode();
        var good = r.LastCode("Your verification code");

        for (var i = 1; i <= 5; i++) Assert.IsType<BadRequestObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(Wrong(good))));
        var blocked = Assert.IsType<ObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(good))); // even the right code
        Assert.Equal(429, blocked.StatusCode);
        Assert.Equal("Too many attempts, wait 15 minutes.", Message(blocked));
        Assert.False((await r.Users.FindByIdAsync(user.Id))!.EmailConfirmed);

        r.Cache.Remove("verify-tries:" + user.Id); // 15 minutes later
        Ok<OkObjectResult>(await account.VerifyEmail(new VerifyEmailRequest(good)));
        Assert.True((await r.Users.FindByIdAsync(user.Id))!.EmailConfirmed);
    }

    [Fact]
    public async Task Review_text_is_refused_until_the_email_is_verified_with_a_code()
    {
        using var r = new Rig();
        r.P.GetRequiredService<AppDbContext>().Titles.Add(new Title { Id = 1, AniListId = 1, Name = "Test" });
        await r.P.GetRequiredService<AppDbContext>().SaveChangesAsync();
        var user = await r.AddUser("kai", confirmed: false);
        var (session, _) = await r.Sessions.Create(user, "phone", "1.1.1.1");
        var review = new RatingRequest(8, 8, 8, 8, "nice");

        var blocked = Assert.IsType<ObjectResult>(await r.Ratings(user).Put(1, review));
        Assert.Equal(403, blocked.StatusCode);
        Assert.Equal("Verify your email to write reviews.", Message(blocked));
        Ok<OkObjectResult>(await r.Ratings(user).Put(1, new RatingRequest(8, 8, 8, 8, ""))); // a plain rating is fine

        await r.Account(user, session.Id).SendVerifyCode();
        Ok<OkObjectResult>(await r.Account(user, session.Id).VerifyEmail(new VerifyEmailRequest(r.LastCode("Your verification code"))));

        Ok<OkObjectResult>(await r.Ratings(user).Put(1, review));
    }

    [Fact]
    public async Task Forgot_password_mails_a_code_once_a_minute_per_address_with_the_same_answer()
    {
        using var r = new Rig();
        await r.AddUser("kai", confirmed: false);

        var first = Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("kai@example.com")));
        var again = Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("KAI@example.com"))); // same address, any case
        var mail = r.Mail.Sent.Single();
        Assert.Equal("kai@example.com", mail.To);
        Assert.Equal("Your password reset code", mail.Subject);
        Assert.Matches("^[0-9]{6}$", r.LastCode("Your password reset code"));
        Assert.DoesNotContain("http", mail.Text); // a code, not a link
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(again.Value));
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("nobody@example.com"))).Value));
        Assert.Single(r.Mail.Sent);

        r.Cache.Remove("forgot-password:KAI@EXAMPLE.COM"); // a minute later
        Ok<OkObjectResult>(await r.Auth().ForgotPassword(new EmailRequest("kai@example.com")));
        Assert.Equal(2, r.Mail.Sent.Count);
    }

    [Fact]
    public async Task The_reset_code_check_answers_ok_true_or_false_and_stops_after_5_wrong_tries()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai");
        await r.Auth().ForgotPassword(new EmailRequest("kai@example.com"));
        var good = r.LastCode("Your password reset code");
        bool IsOk(IActionResult res) => Body(Ok<OkObjectResult>(res)).GetProperty("ok").GetBoolean();

        Assert.True(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", good))));
        Assert.True(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest(" KAI@example.com ", good[..3] + " " + good[3..])))); // case and spaces
        Assert.False(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", ""))));
        Assert.False(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("nobody@example.com", good)))); // same answer as a wrong code
        // a verification code is no reset code
        Assert.False(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", await r.MakeCode(user, EmailCodes.Verify)))));

        // kai's address has used 2 tries since the last right code (the empty one and the verification code); 3 more are allowed
        for (var i = 0; i < 3; i++) Assert.False(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", Wrong(good)))));
        var blocked = Assert.IsType<ObjectResult>(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", good)));
        Assert.Equal(429, blocked.StatusCode);
        Assert.Equal("Too many attempts, wait 15 minutes.", Message(blocked));
        // the password step is refused too, and so is another spelling of the same address
        Assert.Equal(429, Status(await r.Auth().ResetPassword(new ResetPasswordRequest("kai@example.com", good, NewPass))));
        Assert.Equal(429, Status(await r.Auth().VerifyResetCode(new ResetCodeRequest(" Kai@Example.com", good))));
        Assert.True(await r.Users.CheckPasswordAsync(user, Pass)); // nothing changed

        // an address with no account is limited the same way: its first try was above, 4 more are allowed
        for (var i = 0; i < 4; i++) Assert.False(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("nobody@example.com", "123456"))));
        Assert.Equal(429, Status(await r.Auth().VerifyResetCode(new ResetCodeRequest("nobody@example.com", "123456"))));

        r.Cache.Remove("reset-tries:KAI@EXAMPLE.COM"); // 15 minutes later
        Assert.True(IsOk(await r.Auth().VerifyResetCode(new ResetCodeRequest("kai@example.com", good))));
    }

    [Fact]
    public async Task Reset_password_with_the_code_sets_it_confirms_the_email_clears_lockout_and_ends_all_sessions()
    {
        using var r = new Rig();
        var user = await r.AddUser("kai", confirmed: false);
        await r.Sessions.Create(user, "phone", "1.1.1.1");
        await r.Sessions.Create(user, "laptop", "2.2.2.2");
        for (var i = 0; i < 10; i++) await r.Auth().Login(new LoginRequest("kai@example.com", "wrong"));
        Assert.True(await r.Users.IsLockedOutAsync(user));
        await r.Auth().ForgotPassword(new EmailRequest("kai@example.com"));
        var good = r.LastCode("Your password reset code");

        // a wrong code or a weak password changes nothing, and a weak password keeps the code usable
        var badCode = Assert.IsType<BadRequestObjectResult>(await r.Auth().ResetPassword(new ResetPasswordRequest("kai@example.com", Wrong(good), NewPass)));
        Assert.Equal("Invalid or expired code.", Message(badCode));
        var weak = Assert.IsType<BadRequestObjectResult>(await r.Auth().ResetPassword(new ResetPasswordRequest("kai@example.com", good, "short")));
        Assert.Contains("at least 12", Body(weak).GetProperty("errors").ToString());
        Assert.True(await r.Users.CheckPasswordAsync(user, Pass));
        Assert.Equal(2, (await r.Sessions.Active(user.Id)).Count);

        Ok<OkObjectResult>(await r.Auth().ResetPassword(new ResetPasswordRequest("kai@example.com", good, NewPass)));

        var after = (await r.Users.FindByIdAsync(user.Id))!;
        Assert.True(await r.Users.CheckPasswordAsync(after, NewPass));
        Assert.True(after.EmailConfirmed); // the code proved the inbox
        Assert.False(await r.Users.IsLockedOutAsync(after));
        Assert.Equal(0, after.AccessFailedCount);
        Assert.Empty(await r.Sessions.Active(user.Id));
        Ok<OkObjectResult>(await r.Auth().Login(new LoginRequest("kai@example.com", NewPass)));
        // the password change spent the code
        Assert.IsType<BadRequestObjectResult>(await r.Auth().ResetPassword(new ResetPasswordRequest("kai@example.com", good, "yet another passphrase")));
    }

    [Fact]
    public async Task A_google_only_account_can_set_a_password_with_a_reset_code()
    {
        using var r = new Rig();
        var user = new AppUser { UserName = "gina", Email = "gina@example.com", EmailConfirmed = true };
        Assert.True((await r.Users.CreateAsync(user)).Succeeded);
        Assert.False(await r.Users.HasPasswordAsync(user));

        await r.Auth().ForgotPassword(new EmailRequest("gina@example.com"));
        Ok<OkObjectResult>(await r.Auth().ResetPassword(new ResetPasswordRequest("gina@example.com", r.LastCode("Your password reset code"), NewPass)));

        Assert.True(await r.Users.CheckPasswordAsync(user, NewPass));
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
