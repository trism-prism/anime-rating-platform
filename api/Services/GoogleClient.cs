using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace api.Services;

public record GoogleProfile(string Sub, string Email);

// the server side of Sign in with Google: trades the code for an id_token and checks its claims
public class GoogleClient
{
    readonly HttpClient _http;
    readonly IConfiguration _config;
    readonly ILogger<GoogleClient> _log;

    public GoogleClient(HttpClient http, IConfiguration config, ILogger<GoogleClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public string ClientId => _config["Google:ClientId"] ?? "";
    public bool Configured => ClientId != "" && !string.IsNullOrEmpty(_config["Google:ClientSecret"]);

    // null on any failure; the log gets status codes and exception types only (the code and tokens are secrets)
    public async Task<GoogleProfile?> Exchange(string code, string verifier, string redirectUri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = ClientId,
                    ["client_secret"] = _config["Google:ClientSecret"] ?? "",
                    ["code_verifier"] = verifier,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                })
            };
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Google token request failed: {Status}", (int)response.StatusCode);
                return null;
            }
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return ReadIdToken(body.RootElement.GetProperty("id_token").GetString());
        }
        catch (Exception ex) // network, timeout or an answer that is not what Google sends
        {
            _log.LogWarning("Google sign-in failed: {Type}", ex.GetType().Name);
            return null;
        }
    }

    // The token came straight from Google over TLS, so the signature is not checked
    // (OpenID Connect Core 3.1.3.7). The claims still are.
    GoogleProfile? ReadIdToken(string? idToken)
    {
        var parts = (idToken ?? "").Split('.');
        if (parts.Length != 3)
        {
            _log.LogWarning("Google id_token refused: not-a-jwt");
            return null;
        }
        using var doc = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
        var c = doc.RootElement;

        var iss = Text(c, "iss");
        var verified = c.TryGetProperty("email_verified", out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.String && v.GetString() == "true");
        var live = c.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var secs) && secs > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sub = Text(c, "sub");
        var email = Text(c, "email");

        // fixed reason codes only: the claims are personal data
        var reason = iss != "accounts.google.com" && iss != "https://accounts.google.com" ? "issuer"
            : Text(c, "aud") != ClientId ? "audience"
            : !live ? "expired"
            : !verified ? "email-not-verified"
            : sub == "" || email == "" ? "missing-claim" : null;
        if (reason != null)
        {
            _log.LogWarning("Google id_token refused: {Reason}", reason);
            return null;
        }
        return new GoogleProfile(sub, email);
    }

    static string Text(JsonElement c, string name) =>
        c.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
