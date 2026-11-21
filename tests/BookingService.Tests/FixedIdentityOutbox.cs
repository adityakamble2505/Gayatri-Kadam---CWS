using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Outbox;

namespace GreenCarWash.BookingService.Tests;

internal sealed class FixedIdentityOutbox : IBookingOutbox
{
    public Task<OutboxEvent> CreateAsync(
        Booking booking,
        string eventType,
        string status,
        DateTime occurredAt,
        CancellationToken cancellationToken,
        int? washerId = null,
        string? reason = null,
        int? reviewId = null)
    {
        var washer = washerId ?? booking.WasherId;
        return Task.FromResult(BookingEventPayload.Create(
            booking.BookingId,
            booking.BookingNumber,
            status,
            eventType,
            customerUserId: 3,
            washerUserId: washer is null ? null : 4,
            occurredAt,
            reason,
            reviewId));
    }
}
