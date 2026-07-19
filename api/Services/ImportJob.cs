namespace api.Services;

public record ImportStatus(bool Running, DateTime? StartedAt, DateTime? FinishedAt, ImportCounts? LastCounts, string? LastError);

// runs the import in the background (it takes minutes); one at a time. Registered as a singleton.
public class ImportJob
{
    readonly IServiceScopeFactory _scopes;
    readonly ILogger<ImportJob> _log;
    readonly object _lock = new();
    ImportStatus _status = new(false, null, null, null, null);

    public ImportJob(IServiceScopeFactory scopes, ILogger<ImportJob> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public ImportStatus Status { get { lock (_lock) return _status; } }

    // false when one is already running
    public bool TryStart(int pagesPerType = 6)
    {
        lock (_lock)
        {
            if (_status.Running) return false;
            _status = _status with { Running = true, StartedAt = DateTime.UtcNow, FinishedAt = null, LastError = null };
        }
        _ = Task.Run(async () =>
        {
            ImportCounts? counts = null;
            string? error = null;
            try
            {
                using var scope = _scopes.CreateScope();
                counts = await scope.ServiceProvider.GetRequiredService<AniListImporter>().Import(pagesPerType);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Import failed");
                error = ex.Message;
            }
            lock (_lock)
                _status = _status with { Running = false, FinishedAt = DateTime.UtcNow, LastCounts = counts ?? _status.LastCounts, LastError = error };
        });
        return true;
    }
}
