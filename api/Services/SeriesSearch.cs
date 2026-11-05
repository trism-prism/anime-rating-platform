using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public static class SeriesSearch
{
    // series whose own name or any entry name starts with (prefix) or contains the text.
    // Npgsql gets ILIKE, which the trigram indexes on "Name" serve; InMemory (tests) has no ILike.
    public static IQueryable<Series> Match(this IQueryable<Series> q, AppDbContext db, string text, bool prefix)
    {
        if (db.Database.IsNpgsql())
        {
            var p = (prefix ? "" : "%") + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            return q.Where(s => EF.Functions.ILike(s.Name, p) || s.Titles.Any(t => EF.Functions.ILike(t.Name, p)));
        }
        var l = text.ToLower();
        return prefix
            ? q.Where(s => s.Name.ToLower().StartsWith(l) || s.Titles.Any(t => t.Name.ToLower().StartsWith(l)))
            : q.Where(s => s.Name.ToLower().Contains(l) || s.Titles.Any(t => t.Name.ToLower().Contains(l)));
    }
}

public static class RatingStats
{
    // series rating = average of its entries' averages, skipping entries nobody rated
    public static double? SeriesAverage(IEnumerable<(int Count, double Sum)> entries)
    {
        var averages = entries.Where(e => e.Count > 0).Select(e => e.Sum / e.Count).ToList();
        return averages.Count == 0 ? null : Math.Round(averages.Average(), 2);
    }
}
