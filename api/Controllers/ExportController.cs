using System.Globalization;
using System.IO.Compression;
using System.Text;
using api.Data;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

public record ExportRequest(string? CurrentPassword, string? Format);

public record RatingRow(string Title, int? Volume, int Story, int Art, int Characters, int Enjoyment, double Overall, string Audience, DateTime CreatedAt);
public record ReviewRow(string Title, int? Volume, string Text, bool Spoiler, string Audience, DateTime CreatedAt);
public record ListEntryRow(string Title, string Type, string Status, int Progress, string Audience, DateOnly? StartedOn, DateOnly? FinishedOn, int RewatchCount);
public record CustomListRow(string Name, string Audience, DateTime CreatedAt, int Items);
public record CustomListItemRow(string List, string Kind, string Name, DateTime AddedAt);

// Your own data as JSON (one file) or CSV (a zip of one file per table): ratings, reviews, list entries, custom lists with dates.
// Like changing the password or email it needs the current password (wrong tries count toward the lockout), and it is
// limited to 5 a hour. Every query is filtered by the signed-in user's id; nothing comes from the request.
[ApiController]
[Authorize]
[Route("me/export")]
public class ExportController : ControllerBase
{
    readonly AppDbContext _db;
    readonly UserManager<AppUser> _users;
    readonly CredentialCheck _check;

    public ExportController(AppDbContext db, UserManager<AppUser> users, CredentialCheck check)
    {
        _db = db;
        _users = users;
        _check = check;
    }

    [HttpPost]
    [EnableRateLimiting("export")]
    public async Task<IActionResult> Export(ExportRequest req)
    {
        var csv = string.Equals(req.Format, "csv", StringComparison.OrdinalIgnoreCase);
        if (!csv && !string.Equals(req.Format, "json", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Format must be json or csv." });

        var me = User.FindFirst("sub")!.Value;
        var user = await _users.FindByIdAsync(me);
        if (user == null) return Unauthorized();
        var check = await _check.Password(user, req.CurrentPassword ?? "");
        if (check != Check.Ok) return CredentialCheck.Refused(check, "Wrong password.");

        var ratings = await _db.Ratings.Where(r => r.UserId == me).OrderBy(r => r.Id)
            .Select(r => new RatingRow(r.Title.Name, r.Volume, r.Story, r.Art, r.Characters, r.Enjoyment, r.Overall, r.Audience.ToString(), r.CreatedAt)).ToListAsync();
        var reviews = await _db.Ratings.Where(r => r.UserId == me && r.ReviewText != null).OrderBy(r => r.Id)
            .Select(r => new ReviewRow(r.Title.Name, r.Volume, r.ReviewText!, r.Spoiler, r.Audience.ToString(), r.CreatedAt)).ToListAsync();
        var entries = await _db.WatchList.Where(w => w.UserId == me).OrderBy(w => w.Id)
            .Select(w => new ListEntryRow(w.Title.Name, w.Title.Type.ToString(), w.Status.ToString(), w.Progress, w.Audience.ToString(), w.StartedOn, w.FinishedOn, w.RewatchCount)).ToListAsync();
        var lists = await _db.CustomLists.Where(l => l.UserId == me).OrderBy(l => l.Id)
            .Select(l => new CustomListRow(l.Name, l.Audience.ToString(), l.CreatedAt, l.Items.Count)).ToListAsync();
        var items = await _db.CustomListItems.Where(i => i.List.UserId == me).OrderBy(i => i.ListId).ThenBy(i => i.Id)
            .Select(i => new CustomListItemRow(i.List.Name, i.TitleId != null ? "Title" : "Series", i.TitleId != null ? i.Title!.Name : i.Series!.Name, i.AddedAt)).ToListAsync();

        Response.Headers.CacheControl = "no-store";
        if (!csv)
            return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new { exportedAt = DateTime.UtcNow, ratings, reviews, listEntries = entries, customLists = lists, customListItems = items },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }),
                "application/json", "anime-platform-export.json");

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            AddCsv(archive, "ratings.csv", ratings);
            AddCsv(archive, "reviews.csv", reviews);
            AddCsv(archive, "list-entries.csv", entries);
            AddCsv(archive, "custom-lists.csv", lists);
            AddCsv(archive, "custom-list-items.csv", items);
        }
        return File(zip.ToArray(), "application/zip", "anime-platform-export.zip");
    }

    static void AddCsv<T>(ZipArchive archive, string name, IEnumerable<T> rows)
    {
        using var w = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(true));
        w.Write(Csv(rows));
    }

    // header from the record's property names; values quoted when needed
    public static string Csv<T>(IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties();
        var sb = new StringBuilder(string.Join(",", props.Select(p => p.Name)) + "\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(",", props.Select(p => Cell(p.GetValue(row)))) + "\r\n");
        return sb.ToString();
    }

    static string Cell(object? v)
    {
        var s = v switch
        {
            null => "",
            DateTime d => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? ""
        };
        // a cell that starts with = + - @ is run as a formula by spreadsheets; a leading apostrophe defuses it (text only: numbers are never negative here)
        if (v is string && s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
