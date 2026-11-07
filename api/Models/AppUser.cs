using Microsoft.AspNetCore.Identity;

namespace api.Models;

public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // shown as "Joined" on the profile
    // when the user confirmed they are 13+ and accepted the Terms, and which version (see Terms.Version); null for accounts older than this rule
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsVersion { get; set; }
    public string? Bio { get; set; } // optional, plain text, at most Avatars.MaxBio characters, censored on write
    // set when the account was deleted: the row stays as an anonymous author of the ratings, reviews and replies left behind
    public DateTime? DeletedAt { get; set; }
    public const string DeletedName = "Deleted user"; // what every page shows instead of UserName
}
