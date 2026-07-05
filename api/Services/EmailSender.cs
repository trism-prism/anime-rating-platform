using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

    public async Task Send(string to, string subject, string body)
    {
        var apiKey = _config["Resend:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            // only Development may log the mail (it holds the confirmation link)
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Resend:ApiKey is not set; cannot send email outside Development.");
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
}
