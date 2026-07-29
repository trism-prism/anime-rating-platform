namespace api.Services;

// The only place the censored words live. Lower case, letters only; Censor handles case, repeated letters and leetspeak.
// Plain swearing is not here on purpose: only slurs and derogatory terms are masked.
public static class SlurList
{
    public static readonly string[] Words =
    {
        "nigger", "nigga", "faggot", "fag", "kike", "spic", "chink", "gook", "wetback", "tranny",
        "retard", "retarded", "coon", "paki", "beaner", "towelhead", "raghead",
    };
}
