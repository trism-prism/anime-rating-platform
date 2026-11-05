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
    public DbSet<Series> Series => Set<Series>();
    public DbSet<TitleRelation> TitleRelations => Set<TitleRelation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // trigram indexes serve the ILIKE '%text%' in SeriesSearch; the extension is created by the migration
        b.HasPostgresExtension("pg_trgm");
        b.Entity<Series>().HasIndex(s => s.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Title>().HasIndex(t => t.Name).HasMethod("gin").HasOperators("gin_trgm_ops");

        b.Entity<Title>().HasIndex(t => t.AniListId).IsUnique();
        b.Entity<Title>().Property(t => t.Type).HasConversion<string>();
        b.Entity<Character>().HasIndex(c => c.AniListId).IsUnique();
        b.Entity<VoiceActor>().HasIndex(v => v.AniListId).IsUnique();
        b.Entity<TitleCharacter>().HasIndex(tc => new { tc.TitleId, tc.CharacterId }).IsUnique();
        // Volume is nullable and Postgres treats NULLs as distinct, so two filtered unique indexes
        b.Entity<Rating>().HasIndex(r => new { r.UserId, r.TitleId }).IsUnique().HasFilter("\"Volume\" IS NULL");
        b.Entity<Rating>().HasIndex(r => new { r.UserId, r.TitleId, r.Volume }).IsUnique().HasFilter("\"Volume\" IS NOT NULL");
        b.Entity<Title>().HasOne(t => t.Series).WithMany(s => s.Titles).HasForeignKey(t => t.SeriesId)
            .OnDelete(DeleteBehavior.Restrict); // deleting a series must never take its titles (and their ratings) with it
        b.Entity<Title>().HasIndex(t => new { t.SeriesId, t.Position });
        b.Entity<Title>().Property(t => t.RatingCount).IsConcurrencyToken();
        b.Entity<Title>().Property(t => t.RatingSum).IsConcurrencyToken();
        b.Entity<Series>().Property(s => s.Kind).HasConversion<string>();
        b.Entity<Series>().HasIndex(s => new { s.Popularity, s.Id });
        b.Entity<TitleRelation>().HasIndex(r => new { r.FromAniListId, r.ToAniListId, r.Type }).IsUnique();
        b.Entity<TitleRelation>().HasIndex(r => r.ToAniListId);
        b.Entity<WatchListEntry>().HasIndex(w => new { w.UserId, w.TitleId }).IsUnique();
        b.Entity<WatchListEntry>().Property(w => w.Status).HasConversion<string>();
        b.Entity<Session>().HasIndex(s => s.UserId);
        // two refreshes with the same token: only the first update finds this hash still in place
        b.Entity<Session>().Property(s => s.CurrentTokenHash).IsConcurrencyToken();    }
}
