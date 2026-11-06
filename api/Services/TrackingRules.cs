namespace api.Services;

// small rules for list entries and custom lists
public static class TrackingRules
{
    public const int MaxRewatches = 1000;

    // null when fine, otherwise the message. "Not in the future" allows tomorrow UTC so a user ahead of UTC can enter their today.
    public static string? CheckDates(DateOnly? started, DateOnly? finished, int rewatches)
    {
        var limit = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        if (started > limit || finished > limit) return "Dates can't be in the future.";
        if (started != null && finished != null && finished < started) return "The finish date can't be before the start date.";
        if (rewatches < 0 || rewatches > MaxRewatches) return $"Rewatch count must be between 0 and {MaxRewatches}.";
        return null;
    }
}
