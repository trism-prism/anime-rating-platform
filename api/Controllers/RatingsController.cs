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
    readonly AppDbContext _db;

    public RatingsController(AppDbContext db) => _db = db;

    // MapInboundClaims is off, so the id is the raw "sub" claim; never read it from the request
    string UserId => User.FindFirst("sub")!.Value;

    [HttpPut("titles/{id:int}/rating")]
    public async Task<IActionResult> Put(int id, RatingRequest body)
    {
        foreach (var v in new[] { body.Story, body.Art, body.Characters, body.Enjoyment })
            if (v < 1 || v > 10) return BadRequest("Each score must be between 1 and 10.");
        if (!await _db.Titles.AnyAsync(t => t.Id == id)) return NotFound();

        var userId = UserId;
        var r = await _db.Ratings.FirstOrDefaultAsync(x => x.UserId == userId && x.TitleId == id);
        if (r == null)
        {
            r = new Rating { UserId = userId, TitleId = id };
            _db.Ratings.Add(r);
        }
        r.Story = body.Story;
        r.Art = body.Art;
        r.Characters = body.Characters;
        r.Enjoyment = body.Enjoyment;
        r.Overall = RatingMath.Overall(body.Story, body.Art, body.Characters, body.Enjoyment);
        r.ReviewText = string.IsNullOrWhiteSpace(body.ReviewText) ? null : body.ReviewText.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { r.Id, r.TitleId, r.Story, r.Art, r.Characters, r.Enjoyment, r.Overall, r.ReviewText, r.CreatedAt });
    }

    [HttpDelete("titles/{id:int}/rating")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = UserId;
        var r = await _db.Ratings.FirstOrDefaultAsync(x => x.UserId == userId && x.TitleId == id);
        if (r == null) return NotFound();
        _db.Ratings.Remove(r);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("me/ratings")]
    public async Task<IActionResult> Mine()
    {
        var userId = UserId;
        var list = await _db.Ratings.Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.TitleId, titleName = r.Title.Name, coverUrl = r.Title.CoverUrl,
                r.Story, r.Art, r.Characters, r.Enjoyment, r.Overall, r.ReviewText, r.CreatedAt
            }).ToListAsync();
        return Ok(list);
    }
}
