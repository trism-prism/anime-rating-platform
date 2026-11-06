using api.Data;
using api.Services;
using System.Text;
using api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT; listen on it
var port = Environment.GetEnvironmentVariable("PORT");
if (port != null) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Railway gives DATABASE_URL (postgres://...); use it when no connection string is set
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrEmpty(connectionString))
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl)) connectionString = DatabaseUrl.ToNpgsql(databaseUrl);
}

// an empty or short key would make every token forgeable, so stop at startup
if (!args.Contains("import") && (builder.Configuration["Jwt:Key"] ?? "").Length < 32)
    throw new InvalidOperationException("Jwt:Key (env Jwt__Key) must be set and at least 32 characters long.");

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    o.ForwardLimit = 2; // requests pass Vercel then Railway, so X-Forwarded-For is "client, vercelIp": read both hops to get the client
    o.KnownNetworks.Clear(); // Railway's proxy isn't loopback
    o.KnownProxies.Clear();
});

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<AniListClient>();
builder.Services.AddScoped<AniListImporter>();
builder.Services.AddSingleton<ImportJob>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<CredentialCheck>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(connectionString));

builder.Services.AddHttpClient<EmailSender>();
builder.Services.AddHttpClient<GoogleClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<PwnedPasswordValidator>(c => c.Timeout = TimeSpan.FromSeconds(3));

builder.Services.AddIdentityCore<AppUser>(IdentitySetup.Configure)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// after AddIdentityCore, or it would replace the built-in length rule. Resolved through the typed
// client so the timeout applies (AddPasswordValidator would inject a plain HttpClient).
builder.Services.AddScoped<IPasswordValidator<AppUser>>(sp => sp.GetRequiredService<PwnedPasswordValidator>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub" and "email" as written in the token
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "")),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30) // the default 5 minutes would stretch the 15 minute token to 20
        };
        // a revoked or expired session kills its access token at once (one lookup per request)
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var sessions = ctx.HttpContext.RequestServices.GetRequiredService<SessionService>();
                if (!await sessions.IsActive(ctx.Principal?.FindFirst("sid")?.Value)) ctx.Fail("Session ended.");
            }
        };
    });
builder.Services.AddAuthorization();

// auth endpoints: 10 requests per minute per IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = (ctx, _) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(after.TotalSeconds)).ToString();
        return ValueTask.CompletedTask;
    };
    // social writes: per signed-in user, so one account cannot spam requests or blocks
    o.AddPolicy("friend-request", ctx => SocialRules.PerUser(ctx, 20, TimeSpan.FromHours(1)));
    o.AddPolicy("block", ctx => SocialRules.PerUser(ctx, 30, TimeSpan.FromHours(1)));
    o.AddPolicy("notify", ctx => SocialRules.PerUser(ctx, 120, TimeSpan.FromMinutes(1)));
    o.AddPolicy("reply", ctx => SocialRules.PerUser(ctx, 30, TimeSpan.FromHours(1)));
    o.AddPolicy("reaction", ctx => SocialRules.PerUser(ctx, 120, TimeSpan.FromMinutes(1)));
    o.AddPolicy("audience", ctx => SocialRules.PerUser(ctx, 120, TimeSpan.FromMinutes(1)));
    o.AddPolicy("list-write", ctx => SocialRules.PerUser(ctx, 120, TimeSpan.FromMinutes(1)));
    o.AddPolicy("report", ctx => SocialRules.PerUser(ctx, 10, TimeSpan.FromHours(1)));
    o.AddPolicy("profile", ctx => SocialRules.PerUser(ctx, 60, TimeSpan.FromMinutes(1))); // per user, per IP when anonymous
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var origins = (builder.Configuration["Cors:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// dotnet run -- import
if (args.Contains("import"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    try
    {
        // takes the same Postgres advisory lock as the admin import, so the two cannot overlap
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<AniListImporter>().Import());
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

// create the Admin role; the first admin is promoted at login (see AdminBootstrap)
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    try
    {
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));
    }
    catch (Exception ex) // database not up or not migrated yet
    {
        app.Logger.LogWarning("Admin role setup skipped: {Message}", ex.Message);
    }
}

// give accounts whose username is still their email a generated one
using (var scope = app.Services.CreateScope())
{
    try
    {
        await UserNames.FixEmailNames(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Username fix skipped: {Message}", ex.Message);
    }
}

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment()) app.UseHttpsRedirection(); // in production Railway's proxy handles HTTPS

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication: the social policies are keyed by user id
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => "ok");

app.Run();
