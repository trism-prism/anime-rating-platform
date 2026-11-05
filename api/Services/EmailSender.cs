using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace api.Services;

public class EmailSender
{
    readonly IConfiguration _config;
    readonly ILogger<EmailSender> _log;
    readonly IHostEnvironment _env;
    readonly HttpClient _http;

    public EmailSender(IConfiguration config, ILogger<EmailSender> log, IHostEnvironment env, HttpClient http)
    {
        _config = config;
        _log = log;
        _env = env;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(15); // the default is 100 s
    }

    public async Task SendConfirmation(UserManager<AppUser> users, AppUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{_config["App:FrontendUrl"]}/confirm-email?userId={user.Id}&token={encoded}";
        await Send(user.Email!, "Confirm your email", $"Confirm your account: {link}");
    }

    public async Task SendPasswordReset(UserManager<AppUser> users, AppUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{_config["App:FrontendUrl"]}/reset-password?userId={user.Id}&token={encoded}";
        await Send(user.Email!, "Reset your password", $"Reset your password: {link}");
    }

    // the six-digit code for the second login step; the code is a secret, so only Send's Development fallback logs it
    public async Task SendSignInCode(UserManager<AppUser> users, AppUser user)
    {
        var code = await users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
        await Send(user.Email!, "Your sign-in code", $"Your sign-in code is {code}. It expires in a few minutes. If you did not try to log in, change your password.");
    }

    // the link goes to the new address; the token only fits that address
    public async Task SendEmailChange(UserManager<AppUser> users, AppUser user, string newEmail)
    {
        var token = await users.GenerateChangeEmailTokenAsync(user, newEmail);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{_config["App:FrontendUrl"]}/confirm-email-change?userId={user.Id}&newEmail={Uri.EscapeDataString(newEmail)}&token={encoded}";
        await Send(newEmail, "Confirm your new email", $"Confirm your new email address: {link}");
    }

    public async Task Send(string to, string subject, string body)
    {
        if (!string.IsNullOrEmpty(_config["Gmail:RefreshToken"]))
        {
            await SendWithGmail(to, subject, body);
            return;
        }

        var apiKey = _config["Resend:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            // only Development may log the mail (it holds the confirmation link)
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("No email sender is set (Gmail:RefreshToken or Resend:ApiKey); cannot send email outside Development.");
            _log.LogInformation("Email to {To}: {Subject}\n{Body}", to, subject, body);
            return;
        }

        var from = string.IsNullOrEmpty(_config["Resend:From"]) ? "onboarding@resend.dev" : _config["Resend:From"];
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new { from, to = new[] { to }, subject, text = body })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _http.SendAsync(request);
        // status only: the response and request hold the link and the key
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Resend rejected the email: {(int)response.StatusCode} {response.StatusCode}");
    }

    // one cached access token, replaced whole (EmailSender is transient); keyed by refresh token
    record CachedToken(string RefreshToken, string AccessToken, DateTime Expires);
    static CachedToken? _cached;

    async Task<string> GetGmailAccessToken(string refreshToken)
    {
        var cached = _cached;
        if (cached != null && cached.RefreshToken == refreshToken && cached.Expires > DateTime.UtcNow)
            return cached.AccessToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _config["Gmail:ClientId"] ?? "",
                ["client_secret"] = _config["Gmail:ClientSecret"] ?? "",
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gmail token request failed: {(int)response.StatusCode} {response.StatusCode}");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var seconds = json.RootElement.GetProperty("expires_in").GetInt32();
        _cached = new CachedToken(refreshToken, token, DateTime.UtcNow.AddSeconds(seconds - 60));
        return token;
    }

    async Task SendWithGmail(string to, string subject, string body)
    {
        // CR/LF in a header value would let the caller add headers
        if (to.Contains('\r') || to.Contains('\n') || subject.Contains('\r') || subject.Contains('\n'))
            throw new ArgumentException("Email address and subject must not contain line breaks.");
        var from = _config["Gmail:From"];
        if (string.IsNullOrEmpty(from))
            throw new InvalidOperationException("Gmail:From is not set (for example: Anime Rating Platform <you@gmail.com>).");

        var token = await GetGmailAccessToken(_config["Gmail:RefreshToken"]!);

        var bodyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        var lines = Enumerable.Range(0, (bodyB64.Length + 75) / 76)
            .Select(i => bodyB64.Substring(i * 76, Math.Min(76, bodyB64.Length - i * 76)));
        var message = string.Join("\r\n", new[]
        {
            $"From: {from}",
            $"To: {to}",
            $"Subject: =?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=UTF-8",
            "Content-Transfer-Encoding: base64",
            "",
            string.Join("\r\n", lines)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = JsonContent.Create(new { raw = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(message)) })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        // status only: the request holds the link and the token
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gmail rejected the email: {(int)response.StatusCode} {response.StatusCode}");
    }
}
