using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace api.Services;

// the emailed 6-digit codes (verify the address, reset the password) and a cap on tries, kept in the memory cache
public static class EmailCodes
{
    // the token purposes: a code made for one cannot be used for the other
    public const string Verify = "verify-email";
    public const string Reset = "reset-password";

    const int MaxTries = 5;
    static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    static readonly object Gate = new();

    class Tries { public int Count; }

    // Counts one try before the code is checked, so parallel guesses cannot slip past the cap.
    // false once 5 tries were used; the key stays refused until the window (set by the first try) ends.
    // ponytail: in memory, so a restart forgets the count; move it to the database if that matters
    public static bool Try(IMemoryCache cache, string key)
    {
        lock (Gate)
        {
            var t = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = Window; return new Tries(); })!;
            return ++t.Count <= MaxTries;
        }
    }

    // a right code gives the tries back
    public static void Clear(IMemoryCache cache, string key)
    {
        lock (Gate) cache.Remove(key);
    }

    // spaces are ignored and an empty code never passes
    public static async Task<bool> Right(UserManager<AppUser> users, AppUser user, string purpose, string? code)
    {
        var c = (code ?? "").Replace(" ", "");
        return c.Length > 0 && await users.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, purpose, c);
    }
}
