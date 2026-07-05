using System.Net;
using System.Net.Mail;
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

    public EmailSender(IConfiguration config, ILogger<EmailSender> log, IHostEnvironment env)
    {
        _config = config;
        _log = log;
        _env = env;
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
        var password = _config["Smtp:Password"];
        if (string.IsNullOrEmpty(password))
        {
            // only Development may log the mail (it holds the confirmation link)
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Smtp:Password is not set; cannot send email outside Development.");
            _log.LogInformation("Email to {To}: {Subject}\n{Body}", to, subject, body);
            return;
        }

        var user = _config["Smtp:User"];
        using var client = new SmtpClient(_config["Smtp:Host"] ?? "smtp.gmail.com", _config.GetValue("Smtp:Port", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(user, password),
            Timeout = 15000 // fail fast on network problems, the default is 100 s
        };
        var from = string.IsNullOrEmpty(_config["Smtp:From"]) ? user! : _config["Smtp:From"]!;
        await client.SendMailAsync(new MailMessage(from, to, subject, body));
    }
}
