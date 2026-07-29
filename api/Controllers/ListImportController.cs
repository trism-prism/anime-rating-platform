using System.Text.RegularExpressions;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers;

public record AniListImportRequest(string? UserName);

// Import your list from AniList by its public username (no sign-in with AniList, so nothing secret is involved; a private list
// can't be read). One import per user per 10 minutes; it runs in the background, poll GET for the result.
// The OAuth variant (read a private list) needs an AniList client id and is not built.
[ApiController]
[Authorize]
[Route("me/import/anilist")]
public class ListImportController : ControllerBase
{
    // AniList names are letters, digits and underscores, up to 20
    static readonly Regex Name = new(@"^[A-Za-z0-9_]{2,20}\z");

    string Me => User.FindFirst("sub")!.Value;

    [HttpPost]
    [EnableRateLimiting("anilist-import")]
    public IActionResult Start(AniListImportRequest req, [FromServices] ListImportJob job)
    {
        var name = req.UserName?.Trim().TrimStart('@');
        if (name == null || !Name.IsMatch(name)) return BadRequest(new { message = "Enter your AniList username (letters, digits and underscores)." });
        if (!job.TryStart(Me, name, out _))
            return StatusCode(429, new { message = "An import is already running or the server is busy. Try again in a few minutes." });
        return Accepted(job.Status(Me));
    }

    [HttpGet]
    public IActionResult State([FromServices] ListImportJob job) => Ok(job.Status(Me) ?? new ListImportStatus(false, null, null, null, null));
}
