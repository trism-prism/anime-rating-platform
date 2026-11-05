using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record RatingRequest(int Story, int Art, int Characters, int Enjoyment, string? ReviewText);

public static class RatingMath
{
    public static double Overall(int story, int art, int characters, int enjoyment) =>
        Math.Round((story + art + characters + enjoyment) / 4.0, 2);
}

[ApiController]
[Authorize]
public class RatingsController : ControllerBase
{
    // RatingCount and RatingSum are concurrency tokens: when two ratings hit one title at once, the loser reloads and retries
    const int Retries = 4;

    readonly AppDbContext _db;

    public RatingsController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the raw "sub" claim; never read it from the request
    string UserId => User.FindFirst("sub")!.Value;

    // volume: only for manga whose volume count is known, 1..Volumes; omit it to rate the whole title
    [HttpPut("titles/{id:int}/rating")]
    public async Task<IActionResult> Put(int id, RatingRequest body, int? volume = null)
    {
        foreach (var v in new[] { body.Story, body.Art, body.Characters, body.Enjoyment })
            if (v < 1 || v > 10) return BadRequest("Each score must be between 1 and 10.");
        if (body.ReviewText?.Length > 2000) return BadRequest("Review must be 2000 characters or fewer.");

        var userId = UserId;
        // only verified accounts may post public text
        if (!string.IsNullOrWhiteSpace(body.ReviewText) && !await _db.Users.AnyAsync(u => u.Id == userId && u.EmailConfirmed))
            return StatusCode(403, new { message = "Verify your email to write reviews." });

        for (var attempt = 0; ; attempt++)
        {
            var title = await _db.Titles.FirstOrDefaultAsync(t => t.Id == id);
            if (title == null) return NotFound();
            if (volume != null && (title.Volumes == null || volume < 1 || volume > title.Volumes))
                return BadRequest("Volume must be between 1 and the number of volumes, and only for titles with a known volume count.");

            var overall = RatingMath.Overall(body.Story, body.Art, body.Characters, body.Enjoyment);
            var r = await _db.Ratings.FirstOrDefaultAsync(x => x.UserId == userId && x.TitleId == id && x.Volume == volume);
            if (r == null)
            {
                r = new Rating { UserId = userId, TitleId = id, Volume = volume };
                _db.Ratings.Add(r);
                title.RatingCount++;
                title.RatingSum += overall;
            }
            else title.RatingSum += overall - r.Overall;
            r.Story = body.Story;
            r.Art = body.Art;
            r.Characters = body.Characters;
            r.Enjoyment = body.Enjoyment;
            r.Overall = overall;
            r.ReviewText = string.IsNullOrWhiteSpace(body.ReviewText) ? null : body.ReviewText.Trim();

            try
            {
                await _db.SaveChangesAsync();
                return Ok(new { r.Id, r.TitleId, r.Volume, r.Story, r.Art, r.Characters, r.Enjoyment, r.Overall, r.ReviewText, r.CreatedAt });
            }
            catch (DbUpdateConcurrencyException) when (attempt < Retries)
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    [HttpDelete("titles/{id:int}/rating")]
    public async Task<IActionResult> Delete(int id, int? volume = null)
    {
        var userId = UserId;
        for (var attempt = 0; ; attempt++)
        {
            var r = await _db.Ratings.FirstOrDefaultAsync(x => x.UserId == userId && x.TitleId == id && x.Volume == volume);
            if (r == null) return NotFound();
            var title = await _db.Titles.FirstAsync(t => t.Id == id);
            title.RatingCount--;
            title.RatingSum = title.RatingCount == 0 ? 0 : title.RatingSum - r.Overall;
            _db.Ratings.Remove(r);

            try
            {
                await _db.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateConcurrencyException) when (attempt < Retries)
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    [HttpGet("me/ratings")]
    public async Task<IActionResult> Mine()
    {
        var userId = UserId;
        var list = await _db.Ratings.Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.TitleId, r.Volume, titleName = r.Title.Name, coverUrl = r.Title.CoverUrl, seriesId = r.Title.SeriesId,
                r.Story, r.Art, r.Characters, r.Enjoyment, r.Overall, r.ReviewText, r.CreatedAt
            }).ToListAsync();
        return Ok(list);
    }
}
