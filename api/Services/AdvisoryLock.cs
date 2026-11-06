using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace api.Services;

// A Postgres session-level advisory lock held on the context's connection, which stays open until disposed.
// Does nothing on other providers (tests on InMemory). Locks are re-entrant within one session, so the same
// context can take the same lock again in nested calls (each dispose releases one hold).
public sealed class AdvisoryLock : IAsyncDisposable
{
    // one import at a time, across every process (web admin job and CLI)
    public const long ImportKey = 0x616E696D6549;
    // anything that writes Titles.SeriesId or deletes a series: importer assignment, Refresh, admin Move and Merge
    public const long SeriesKey = 0x616E696D6553;
    // the daily cleanup job: one instance at a time
    public const long RetentionKey = 0x616E696D6552;

    readonly DbContext? _db;
    readonly long _key;

    AdvisoryLock(DbContext? db, long key) { _db = db; _key = key; }

    // waits until the lock is free
    public static async Task<AdvisoryLock> Wait(DbContext db, long key)
    {
        if (!db.Database.IsNpgsql()) return new AdvisoryLock(null, key);
        await db.Database.OpenConnectionAsync();
        try { await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})"); }
        catch { await db.Database.CloseConnectionAsync(); throw; }
        return new AdvisoryLock(db, key);
    }

    // null when someone else holds it
    public static async Task<AdvisoryLock?> TryTake(DbContext db, long key)
    {
        if (!db.Database.IsNpgsql()) return new AdvisoryLock(null, key);
        await db.Database.OpenConnectionAsync();
        try
        {
            var got = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"").SingleAsync();
            if (got) return new AdvisoryLock(db, key);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
        await db.Database.CloseConnectionAsync();
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_db == null) return;
        try { await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({_key})"); }
        catch
        {
            // the unlock failed (or the connection broke): the pool must not hand this session out still holding the lock.
            // Swallowed so it never hides the exception that is already unwinding.
            try { NpgsqlConnection.ClearPool((NpgsqlConnection)_db.Database.GetDbConnection()); } catch { }
        }
        finally { await _db.Database.CloseConnectionAsync(); }
    }
}
