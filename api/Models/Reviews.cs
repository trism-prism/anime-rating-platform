namespace api.Models;

// who may see a rating (its score and its review text) or a watch-list entry; aggregates ignore it
public enum Audience { Public, Friends, OnlyMe }

public enum ReactionKind { Helpful, Unhelpful, Funny }

// one flat level under a review; visible to whoever can see the review (minus users blocked either way)
public class Reply
{
    public int Id { get; set; }
    public int RatingId { get; set; }
    public Rating Rating { get; set; } = null!;
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public string Body { get; set; } = "";
    public bool Spoiler { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// one per (review, user); Kind is a concurrency token so two changes by the same user can't both adjust the counters
public class Reaction
{
    public int Id { get; set; }
    public int RatingId { get; set; }
    public Rating Rating { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ReactionKind Kind { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
