namespace GreenCarWash.BookingService.Services;

public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    public string ImmediateStrategy { get; set; } = "Nearest";

    public string ScheduledStrategy { get; set; } = "TopRated";

    public int RetryDelaySeconds { get; set; } = 10;

    public int MaxAttempts { get; set; } = 5;

    public int OfferSeconds { get; set; } = 60;

    public int ScheduledWindowMinutes { get; set; } = 30;

    public TimeSpan OfferDuration => TimeSpan.FromSeconds(Math.Max(0, OfferSeconds));

    public TimeSpan RetryDelay => TimeSpan.FromSeconds(Math.Max(0, RetryDelaySeconds));

    public TimeSpan SessionBudget =>
        TimeSpan.FromSeconds(Math.Max(1, MaxAttempts) * (Math.Max(0, OfferSeconds) + Math.Max(0, RetryDelaySeconds) + 1));
}
