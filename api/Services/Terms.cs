namespace api.Services;

// The version of the Terms of Service text people accept at sign-up. Change it together with TERMS_UPDATED in web/src/pages/Terms.tsx.
// Accounts made before the sign-up checkboxes have no acceptance stored and are covered by "using the site after a change".
public static class Terms
{
    public const string Version = "2026-11-06";
}
