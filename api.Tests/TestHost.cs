using System.Net;
using System.Security.Claims;
using System.Text.Json;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

// Identity on InMemory plus an EmailSender that records the mail instead of sending it (Resend path, fake HTTP handler).
public sealed class TestHost : IDisposable
{
    public record Mail(string To, string Subject, string Body);

    class Recorder : HttpMessageHandler
    {
        public List<Mail> Sent { get; } = new();
        public bool Fail;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            if (Fail) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            Sent.Add(new Mail(json.GetProperty("to")[0].GetString()!, json.GetProperty("subject").GetString()!, json.GetProperty("text").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    readonly ServiceProvider _p;
    readonly Recorder _recorder = new();
    public IConfiguration Config { get; }

    public TestHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddMemoryCache();
        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('k', 40), ["Jwt:Issuer"] = "test", ["Resend:ApiKey"] = "re_test", ["Resend:From"] = "noreply@example.com"
        }).Build();
        services.AddSingleton(Config);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(o => o.SignIn.RequireConfirmedEmail = false)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        _p = services.BuildServiceProvider();
    }

    public List<Mail> Mails => _recorder.Sent;
    public bool MailFails { set => _recorder.Fail = value; }
    public AppDbContext Db => _p.GetRequiredService<AppDbContext>();
    public UserManager<AppUser> Users => _p.GetRequiredService<UserManager<AppUser>>();
    public IMemoryCache Cache => _p.GetRequiredService<IMemoryCache>();
    public IHostEnvironment Env { get; } = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Production };

    public EmailSender Email => new(Config, NullLogger<EmailSender>.Instance, Env, new HttpClient(_recorder));
    public SessionService Sessions => new(Db, Users, Config, Env);

    public async Task<AppUser> AddUser(string name, string? password = "Passw0rd1!", bool admin = false, bool confirmed = true)
    {
        var user = new AppUser { UserName = name, Email = name + "@example.com", EmailConfirmed = confirmed };
        var result = password == null ? await Users.CreateAsync(user) : await Users.CreateAsync(user, password);
        Assert.True(result.Succeeded);
        if (admin)
        {
            var roles = _p.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));
            await Users.AddToRoleAsync(user, "Admin");
        }
        return user;
    }

    public T As<T>(T controller, AppUser? user) where T : ControllerBase
    {
        var identity = user == null ? new ClaimsIdentity() : new ClaimsIdentity(new[] { new Claim("sub", user.Id) }, "test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    public void Dispose() => _p.Dispose();
}
