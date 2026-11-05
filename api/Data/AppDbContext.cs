using api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Title> Titles => Set<Title>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<VoiceActor> VoiceActors => Set<VoiceActor>();
    public DbSet<TitleCharacter> TitleCharacters => Set<TitleCharacter>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<WatchListEntry> WatchList => Set<WatchListEntry>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Title>().HasIndex(t => t.AniListId).IsUnique();
        b.Entity<Title>().Property(t => t.Type).HasConversion<string>();
        b.Entity<Character>().HasIndex(c => c.AniListId).IsUnique();
        b.Entity<VoiceActor>().HasIndex(v => v.AniListId).IsUnique();
        b.Entity<TitleCharacter>().HasIndex(tc => new { tc.TitleId, tc.CharacterId }).IsUnique();
        b.Entity<Rating>().HasIndex(r => new { r.UserId, r.TitleId }).IsUnique();
        b.Entity<WatchListEntry>().HasIndex(w => new { w.UserId, w.TitleId }).IsUnique();
        b.Entity<WatchListEntry>().Property(w => w.Status).HasConversion<string>();
        b.Entity<Session>().HasIndex(s => s.UserId);
        // two refreshes with the same token: only the first update finds this hash still in place
        b.Entity<Session>().Property(s => s.CurrentTokenHash).IsConcurrencyToken();
        b.Entity<SecurityEvent>().HasIndex(e => new { e.UserId, e.At });
    }
}
