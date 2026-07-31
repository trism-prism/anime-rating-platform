using System.Text.Json;
using api.Services;
using Microsoft.AspNetCore.Http;

namespace api.Tests;

public class SecurityHeadersTests
{
    static IHeaderDictionary Headers(bool https)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = https ? "https" : "http";
        SecurityHeaders.Apply(ctx);
        return ctx.Response.Headers;
    }

    [Fact]
    public void Every_response_gets_the_basic_headers_and_HSTS_only_over_https()
    {
        var h = Headers(https: true);
        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", h["Referrer-Policy"]);
        Assert.Contains("frame-ancestors 'none'", h["Content-Security-Policy"].ToString());
        Assert.Equal("DENY", h["X-Frame-Options"]);
        Assert.Contains("camera=()", h["Permissions-Policy"].ToString());
        Assert.StartsWith("max-age=", h["Strict-Transport-Security"].ToString());

        Assert.False(Headers(https: false).ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public void The_docs_page_gets_a_policy_that_lets_it_run_but_still_forbids_framing()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/docs/index.html";
        SecurityHeaders.Apply(ctx);
        var csp = ctx.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);

        ctx = new DefaultHttpContext();
        ctx.Request.Path = "/documents"; // only the /docs segment, not a prefix match
        SecurityHeaders.Apply(ctx);
        Assert.StartsWith("default-src 'none'", ctx.Response.Headers["Content-Security-Policy"].ToString());
    }

    // the website's headers live in web/vercel.json; keep the API rewrite and the single-page fallback next to them
    [Fact]
    public void Vercel_config_has_headers_and_keeps_the_rewrites()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "web", "vercel.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "web", "vercel.json")));

        var keys = json.RootElement.GetProperty("headers")[0].GetProperty("headers").EnumerateArray().ToDictionary(h => h.GetProperty("key").GetString()!, h => h.GetProperty("value").GetString()!);
        foreach (var key in new[] { "Content-Security-Policy", "Strict-Transport-Security", "X-Content-Type-Options", "Referrer-Policy", "Permissions-Policy" })
            Assert.True(keys.ContainsKey(key), key);
        var csp = keys["Content-Security-Policy"];
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe", csp); // no inline or eval scripts: index.html has none

        var rewrites = json.RootElement.GetProperty("rewrites").EnumerateArray().Select(r => r.GetProperty("source").GetString()).ToList();
        Assert.Equal(new[] { "/api/:path*", "/(.*)" }, rewrites);
    }
}
