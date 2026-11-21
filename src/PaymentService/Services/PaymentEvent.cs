using System.Globalization;
using System.Text.Json;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.PaymentService.Entities;

namespace GreenCarWash.PaymentService.Services;

public static class PaymentEvent
{
    public static OutboxEvent Create(
        string eventType,
        Payment payment,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        decimal amount,
        DateTime occurredAt)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventType,
            bookingId,
            bookingNumber,
            customerUserId,
            washerUserId,
            status = payment.PaymentStatus.ToString(),
            referenceId = payment.PaymentId.ToString(CultureInfo.InvariantCulture),
            occurredAt,
            amount
        });
        return new OutboxEvent
        {
            EventType = eventType,
            Payload = payload,
            CreatedAt = occurredAt,
            NextAttemptAt = occurredAt
        };
    }
}
