using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.Extensions.Options;

namespace GreenCarWash.BookingService.Services;

public sealed class DispatchMaintenance
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DispatchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DispatchMaintenance> _logger;

    public DispatchMaintenance(
        IServiceScopeFactory scopes,
        IOptions<DispatchOptions> options,
        IClock clock,
        ILogger<DispatchMaintenance> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> ClaimScheduledAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var claimed = await bookings.ClaimScheduledAsync(_clock.UtcNow, cancellationToken);
        return claimed.Select(booking => booking.BookingId).ToArray();
    }

    public async Task EscalateDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var due = await bookings.EscalationsDueAsync(_clock.UtcNow, _options.ScheduledWindowMinutes, cancellationToken);
        foreach (var booking in due)
            await EscalateAsync(booking.BookingId, "Scheduled dispatch window elapsed", cancellationToken);
    }

    public async Task EscalateAsync(int bookingId, string reason, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var washers = scope.ServiceProvider.GetRequiredService<IWasherDirectory>();
        var booking = await bookings.FindReadAsync(bookingId, cancellationToken);
        if (booking is null || booking.WasherId is not null)
            return;

        var now = _clock.UtcNow;
        await bookings.ExpirePendingOffersAsync(bookingId, now, null, cancellationToken);
        await bookings.FlagAdminAsync(bookingId, reason, now, cancellationToken);

        WasherOffer next;
        try
        {
            next = await washers.NextAsync(
                booking.CityId,
                [],
                _options.ScheduledStrategy,
                booking.Latitude,
                booking.Longitude,
                cancellationToken);
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("No washer available to auto-assign booking {BookingId}", bookingId);
            return;
        }

        var attempt = await bookings.TryAcceptAsync(
            bookingId,
            next.WasherId,
            booking.CityId,
            next.UserId,
            now,
            cancellationToken,
            requireOffer: false,
            clearAttention: false,
            role: "System",
            note: "Auto-assigned");
        if (attempt.Outcome != AcceptOutcome.Accepted)
            return;

        try
        {
            await washers.ConfirmAsync(next.WasherId, bookingId, cancellationToken);
            _logger.LogInformation("Auto-assigned washer {WasherId} to booking {BookingId}", next.WasherId, bookingId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Auto-assign confirm failed for booking {BookingId}", bookingId);
            await bookings.RevertAcceptAsync(bookingId, next.WasherId, attempt.PreviousStatus, _clock.UtcNow, cancellationToken);
        }
    }

    public async Task RemindAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var due = await bookings.RemindersDueAsync(_clock.UtcNow, cancellationToken);
        foreach (var booking in due)
        {
            if (await bookings.HasReminderAsync(booking.BookingId, cancellationToken))
                continue;
            await bookings.AppendEventAsync(booking.BookingId, "Reminder2h", _clock.UtcNow, cancellationToken);
        }
    }
}
