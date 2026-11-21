namespace GreenCarWash.BookingService.Services;

public sealed class ScheduledBookingWorker : BackgroundService
{
    private readonly DispatchMaintenance _maintenance;
    private readonly IDispatchQueue _dispatch;
    private readonly ILogger<ScheduledBookingWorker> _logger;

    public ScheduledBookingWorker(
        DispatchMaintenance maintenance,
        IDispatchQueue dispatch,
        ILogger<ScheduledBookingWorker> logger)
    {
        _maintenance = maintenance;
        _dispatch = dispatch;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        await TickAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TickAsync(stoppingToken);
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var claimed = await _maintenance.ClaimScheduledAsync(cancellationToken);
        foreach (var bookingId in claimed)
            _dispatch.Enqueue(new DispatchRequest(bookingId, DispatchKind.Scheduled));

        await _maintenance.EscalateDueAsync(cancellationToken);
        await _maintenance.RemindAsync(cancellationToken);
        if (claimed.Count > 0)
            _logger.LogInformation("Scheduled dispatch claimed {Count} bookings", claimed.Count);
    }
}
