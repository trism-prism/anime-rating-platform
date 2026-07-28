using api.Data;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

// Direct moderation (the report queue uses the same SocialRules helpers). Records nothing: the review's text goes, its score stays.
[ApiController]
[Authorize]
[AdminOnly]
[Route("admin")]
public class AdminReviewsController : ControllerBase
{
    readonly AppDbContext _db;

    public AdminReviewsController(AppDbContext db) => _db = db;

    [HttpDelete("reviews/{id:int}/text")]
    public async Task<IActionResult> RemoveText(int id) =>
        await SocialRules.RemoveReviewText(_db, id) && await SocialRules.TrySave(_db) ? NoContent() : NotFound();

    [HttpDelete("replies/{id:int}")]
    public async Task<IActionResult> DeleteReply(int id) =>
        await SocialRules.RemoveReply(_db, id) && await SocialRules.TrySave(_db) ? NoContent() : NotFound();
}
