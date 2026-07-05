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
    o.KnownNetworks.Clear(); // Railway's proxy isn't loopback
    o.KnownProxies.Clear();
});

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<AniListClient>();
builder.Services.AddScoped<AniListImporter>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(connectionString));

builder.Services.AddScoped<EmailSender>();

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.SignIn.RequireConfirmedEmail = false;
    o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.AllowedForNewUsers = true;
    o.Password.RequiredLength = 8;
    o.Password.RequireDigit = true;
    o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

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
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization();

// auth endpoints: 10 requests per minute per IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
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
    var counts = await scope.ServiceProvider.GetRequiredService<AniListImporter>().Import();
    Console.WriteLine(counts);
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

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment()) app.UseHttpsRedirection(); // in production Railway's proxy handles HTTPS

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => "ok");

app.Run();
