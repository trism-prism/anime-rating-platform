namespace api.Services;

// Headers for every API response. The API only sends JSON and redirects, so the page policy shuts everything down
// (default-src 'none') and forbids framing. The website gets its own set from web/vercel.json.
public static class SecurityHeaders
{
    public static void Apply(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        // the /docs page (Swagger UI) needs its own scripts and styles; it is a static page with no session of ours behind it
        h["Content-Security-Policy"] = ctx.Request.Path.StartsWithSegments("/docs")
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'"
            : "default-src 'none'; frame-ancestors 'none'";
        h["X-Frame-Options"] = "DENY";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        // only over HTTPS (Railway's proxy says so through X-Forwarded-Proto): browsers ignore it on http, and it would stick to localhost
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
}
