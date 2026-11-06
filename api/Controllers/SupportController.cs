using System.ComponentModel.DataAnnotations;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers;

// Website is the honeypot: a hidden field real visitors never fill
public record SupportRequest(string? Name, string? Email, string? Message, string? Website);

// The contact form. Works signed out. The message is emailed to every admin and stored nowhere.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("support")]
[Route("support")]
public class SupportController : ControllerBase
{
    public const int MaxMessage = 2000, MaxName = 100, MaxEmail = 254;

    readonly UserManager<AppUser> _users;
    readonly EmailSender _email;
    readonly ILogger<SupportController> _log;

    public SupportController(UserManager<AppUser> users, EmailSender email, ILogger<SupportController> log)
    {
        _users = users;
        _email = email;
        _log = log;
    }

    [HttpPost]
    public async Task<IActionResult> Send(SupportRequest req)
    {
        var thanks = Ok(new { message = "Thanks, your message was sent." });
        if (!string.IsNullOrEmpty(req.Website)) return thanks; // a bot filled the hidden field: answer as if it worked, send nothing

        var email = (req.Email ?? "").Trim();
        var message = (req.Message ?? "").Trim();
        var name = (req.Name ?? "").Trim().ReplaceLineEndings(" ");
        if (email.Length > MaxEmail || email.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Enter a valid email address so we can answer." });
        if (message.Length < 1) return BadRequest(new { message = "Write a message." });
        if (message.Length > MaxMessage) return BadRequest(new { message = $"The message must be {MaxMessage} characters or fewer." });
        if (name.Length > MaxName) return BadRequest(new { message = $"The name must be {MaxName} characters or fewer." });

        var admins = (await _users.GetUsersInRoleAsync("Admin")).Where(a => !string.IsNullOrEmpty(a.Email)).ToList();
        var body = $"From: {(name == "" ? "(no name)" : name)} <{email}>\n\n{message}\n\nReply to the address above.";
        var sent = 0;
        foreach (var admin in admins)
        {
            try { await _email.Send(admin.Email!, "Support message", body); sent++; }
            catch (Exception ex) { _log.LogError(ex.GetType().Name + " sending a support message to admin {UserId}", admin.Id); }
        }
        if (sent == 0) return StatusCode(503, new { message = "Could not send your message right now. Try again later." });
        return thanks;
    }
}
