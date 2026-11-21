using System.Globalization;
using System.Text.Json;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Outbox;

namespace GreenCarWash.BookingService.Services;

public interface IBookingOutbox
{
    Task<OutboxEvent> CreateAsync(
        Booking booking,
        string eventType,
        string status,
        DateTime occurredAt,
        CancellationToken cancellationToken,
        int? washerId = null,
        string? reason = null,
        int? reviewId = null);
}

public static class BookingEventPayload
{
    public static readonly string[] EventTypes =
    [
        "BookingCreated",
        "WashRequest",
        "WasherAccepted",
        "ServiceStarted",
        "ServiceCompleted",
        "BookingCancelled",
        "AdminAlert",
        "Reminder2h",
        "RatingSyncPending",
        "RefundPending"
    ];

    public static OutboxEvent Create(
        int bookingId,
        string bookingNumber,
        string status,
        string eventType,
        int customerUserId,
        int? washerUserId,
        DateTime occurredAt,
        string? reason = null,
        int? reviewId = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventType,
            bookingId,
            bookingNumber,
            customerUserId,
            washerUserId,
            status,
            referenceId = bookingId.ToString(CultureInfo.InvariantCulture),
            occurredAt,
            reason
        });
        if (reviewId is int id)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(payload)!.AsObject();
            node["reviewId"] = id;
            payload = node.ToJsonString();
        }
        return new OutboxEvent
        {
            EventType = eventType,
            Payload = payload,
            CreatedAt = occurredAt,
            NextAttemptAt = occurredAt
        };
    }
}

public sealed class BookingOutbox : IBookingOutbox
{
    private readonly ICustomerDirectory _customers;
    private readonly IWasherDirectory _washers;

    public BookingOutbox(ICustomerDirectory customers, IWasherDirectory washers)
    {
        _customers = customers;
        _washers = washers;
    }

    public async Task<OutboxEvent> CreateAsync(
        Booking booking,
        string eventType,
        string status,
        DateTime occurredAt,
        CancellationToken cancellationToken,
        int? washerId = null,
        string? reason = null,
        int? reviewId = null)
    {
        var customer = await _customers.GetByIdAsync(booking.CustomerId, cancellationToken);
        int? washerUserId = null;
        var resolvedWasherId = washerId ?? booking.WasherId;
        if (resolvedWasherId is int id)
        {
            var washer = await _washers.GetByIdAsync(id, cancellationToken);
            washerUserId = washer.UserId;
        }

        return BookingEventPayload.Create(
            booking.BookingId,
            booking.BookingNumber,
            status,
            eventType,
            customer.UserId,
            washerUserId,
            occurredAt,
            reason,
            reviewId);
    }
}
