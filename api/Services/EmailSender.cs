using System.Net;
using System.Net.Mail;

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
            Credentials = new NetworkCredential(user, password)
        };
        var from = string.IsNullOrEmpty(_config["Smtp:From"]) ? user! : _config["Smtp:From"]!;
        await client.SendMailAsync(new MailMessage(from, to, subject, body));
    }
}
