using Microsoft.AspNetCore.Identity;

namespace api.Models;

public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // shown as "Joined" on the profile
    // when the user confirmed they are 13+ and accepted the Terms, and which version (see Terms.Version); null for accounts older than this rule
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsVersion { get; set; }
}
