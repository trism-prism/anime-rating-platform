using System.Net;
using System.Net.Mail;

namespace api.Services;

public class EmailSender
{
    readonly IConfiguration _config;
    readonly ILogger<EmailSender> _log;

    public EmailSender(IConfiguration config, ILogger<EmailSender> log)
    {
        _config = config;
        _log = log;
    }

    public async Task Send(string to, string subject, string body)
    {
        var password = _config["Smtp:Password"];
        // no password set (local dev, tests): log the mail instead of sending it
        if (string.IsNullOrEmpty(password))
        {
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
