using System.Security.Cryptography;
using System.Text;
using api.Models;
using Microsoft.AspNetCore.Identity;

namespace api.Services;

// Refuses passwords found in known breaches (Have I Been Pwned range API).
// Only the first 5 characters of the SHA-1 hash leave the server.
public class PwnedPasswordValidator : IPasswordValidator<AppUser>
{
    readonly HttpClient _http;
    readonly ILogger<PwnedPasswordValidator> _log;

    public PwnedPasswordValidator(HttpClient http, ILogger<PwnedPasswordValidator> log)
    {
        _http = http;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        // too short: the length rule already rejects it, so skip the network call
        if (password == null || password.Length < manager.Options.Password.RequiredLength) return IdentityResult.Success;

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.pwnedpasswords.com/range/" + hash[..5]);
            request.Headers.Add("Add-Padding", "true");
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // lines are "SUFFIX:COUNT"; padding lines have a count of 0
                var parts = line.Split(':');
                if (parts.Length == 2 && parts[1] != "0" && parts[0] == hash[5..])
                    return IdentityResult.Failed(new IdentityError
                    {
                        Code = "PwnedPassword",
                        Description = "This password appears in a known data breach. Pick another one."
                    });
            }
        }
        catch (Exception ex) // fail open: an HIBP outage must not block sign-ups
        {
            _log.LogWarning("Breach check skipped: {Error}", ex.GetType().Name);
        }
        return IdentityResult.Success;
    }
}
