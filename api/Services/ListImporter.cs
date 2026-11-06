using System.Collections.Concurrent;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// Matched = titles we know; Added = new list entries; AlreadyOnList = matched but the user had it (left untouched); Unmatched = not in our database
public record ListImportResult(int Added, int AlreadyOnList, int Unmatched);

// Copies a user's public AniList list into their list here. Titles are matched by AniList id; scores are not imported (a rating here
// has four parts). Existing entries are never changed. New entries are Only me: an import can be big and the user may not want
// all of it public; they can open it up from My list.
public class ListImporter
{
    public const int MaxChunks = 10; // 500 entries each, per type: at most 10,000 entries a type

    readonly AppDbContext _db;
    readonly AniListClient _client;

    public ListImporter(AppDbContext db, AniListClient client)
    {
        _db = db;
        _client = client;
    }

    public async Task<ListImportResult> Run(string userId, string aniListName)
    {
        int added = 0, already = 0, unmatched = 0;
        foreach (var type in new[] { TitleType.Anime, TitleType.Manga })
        {
            var entries = (await _client.GetUserList(aniListName, type.ToString().ToUpper(), MaxChunks)).DistinctBy(e => e.MediaId).ToList();
            foreach (var chunk in entries.Chunk(500))
            {
                var ids = chunk.Select(e => e.MediaId).ToList();
                var titles = await _db.Titles.Where(t => t.Type == type && ids.Contains(t.AniListId)).ToDictionaryAsync(t => t.AniListId, t => t.Id);
                var titleIds = titles.Values.ToList();
                var have = (await _db.WatchList.Where(w => w.UserId == userId && titleIds.Contains(w.TitleId)).Select(w => w.TitleId).ToListAsync()).ToHashSet();

                foreach (var e in chunk)
                {
                    if (!titles.TryGetValue(e.MediaId, out var titleId)) { unmatched++; continue; }
                    if (have.Contains(titleId)) { already++; continue; }
                    var entry = new WatchListEntry
                    {
                        UserId = userId, TitleId = titleId, Status = Map(e.Status), Progress = Math.Max(0, e.Progress),
                        Audience = Audience.OnlyMe, StartedOn = Date(e.StartedAt), FinishedOn = Date(e.CompletedAt), RewatchCount = e.Repeat
                    };
                    // dates and rewatches AniList holds that our rules would refuse are dropped, not the entry
                    if (TrackingRules.CheckDates(entry.StartedOn, entry.FinishedOn, entry.RewatchCount) != null)
                        (entry.StartedOn, entry.FinishedOn, entry.RewatchCount) = (null, null, Math.Clamp(e.Repeat, 0, TrackingRules.MaxRewatches));
                    _db.WatchList.Add(entry);
                    added++;
                }
                await _db.SaveChangesAsync(); // ponytail: a title added by hand mid-import trips the unique index and ends the import; running it again skips what exists
            }
        }
        return new ListImportResult(added, already, unmatched);
    }

    // PAUSED and REPEATING have no twin here: both are still being watched
    static WatchStatus Map(string? s) => s switch
    {
        "PLANNING" => WatchStatus.Planning,
        "COMPLETED" => WatchStatus.Completed,
        "DROPPED" => WatchStatus.Dropped,
        _ => WatchStatus.Watching
    };

    static DateOnly? Date(AListDate? d)
    {
        if (d?.Year is not int y || d.Month is not int m || d.Day is not int day) return null;
        try { return new DateOnly(y, m, day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

public record ListImportStatus(bool Running, DateTime? StartedAt, DateTime? FinishedAt, ListImportResult? Result, string? Error);

// Runs a user's import in the background, one per user and at most MaxRunning at once, so AniList is not hammered.
// Status is kept in memory, per user (lost on restart; the import is safe to run again). Registered as a singleton.
public class ListImportJob
{
    public const int MaxRunning = 3;

    readonly IServiceScopeFactory _scopes;
    readonly ILogger<ListImportJob> _log;
    readonly ConcurrentDictionary<string, ListImportStatus> _status = new();
    int _running;

    public ListImportJob(IServiceScopeFactory scopes, ILogger<ListImportJob> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public ListImportStatus? Status(string userId) => _status.GetValueOrDefault(userId);

    // false: this user already has one running, or the server is busy; the returned task ends when the import does
    public bool TryStart(string userId, string aniListName, out Task done)
    {
        done = Task.CompletedTask;
        if (Interlocked.Increment(ref _running) > MaxRunning) { Interlocked.Decrement(ref _running); return false; }
        var started = new ListImportStatus(true, DateTime.UtcNow, null, null, null);
        if (!ReferenceEquals(_status.AddOrUpdate(userId, started, (_, old) => old.Running ? old : started), started))
        {
            Interlocked.Decrement(ref _running);
            return false;
        }
        done = Task.Run(async () =>
        {
            ListImportResult? result = null;
            string? error = null;
            try
            {
                using var scope = _scopes.CreateScope();
                result = await scope.ServiceProvider.GetRequiredService<ListImporter>().Run(userId, aniListName);
            }
            catch (AniListUserNotFound ex) { error = ex.Message; }
            catch (Exception ex)
            {
                _log.LogError(ex, "AniList list import failed");
                error = "The import failed. Try again later.";
            }
            _status[userId] = new ListImportStatus(false, started.StartedAt, DateTime.UtcNow, result, error);
            Interlocked.Decrement(ref _running);
        });
        return true;
    }
}
