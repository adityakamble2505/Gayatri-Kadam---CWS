using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.Extensions.Options;

namespace GreenCarWash.BookingService.Services;

public sealed class DispatchRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IDispatchQueue _dispatch;
    private readonly DispatchMaintenance _maintenance;
    private readonly DispatchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DispatchRecoveryService> _logger;

    public DispatchRecoveryService(
        IServiceScopeFactory scopes,
        IDispatchQueue dispatch,
        DispatchMaintenance maintenance,
        IOptions<DispatchOptions> options,
        IClock clock,
        ILogger<DispatchRecoveryService> logger)
    {
        _scopes = scopes;
        _dispatch = dispatch;
        _maintenance = maintenance;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RecoverAsync(stoppingToken);

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var washers = scope.ServiceProvider.GetRequiredService<IWasherDirectory>();
        var now = _clock.UtcNow;

        foreach (var offer in await bookings.PendingOffersAsync(cancellationToken))
        {
            if (offer.ExpiresAt > now)
                continue;
            if (!await bookings.ExpireOfferIfPendingAsync(offer.OfferId, now, cancellationToken))
                continue;
            await ReleaseQuietlyAsync(washers, offer.WasherId, cancellationToken);
            _logger.LogInformation("Expired offer {OfferId} for booking {BookingId} during recovery", offer.OfferId, offer.BookingId);
        }

        foreach (var booking in await bookings.PendingImmediateAsync(cancellationToken))
            _dispatch.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));

        var windowCutoff = now.AddMinutes(-_options.ScheduledWindowMinutes);
        foreach (var booking in await bookings.ScheduledInDispatchAsync(cancellationToken))
        {
            if (booking.DispatchStartedAt <= windowCutoff)
                await _maintenance.EscalateAsync(booking.BookingId, "Scheduled dispatch window elapsed", cancellationToken);
            else
                _dispatch.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Scheduled));
        }
    }

    private async Task ReleaseQuietlyAsync(IWasherDirectory washers, int washerId, CancellationToken cancellationToken)
    {
        try
        {
            await washers.ReleaseOfferAsync(washerId, cancellationToken);
        }
        catch (Exception exception) when (exception is ConflictException or NotFoundException or BadGatewayException)
        {
            _logger.LogInformation("Washer {WasherId} had no offer lease during recovery", washerId);
        }
    }
}
