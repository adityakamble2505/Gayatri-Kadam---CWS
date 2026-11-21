using System.Net.Http.Headers;
using System.Text;
using GreenCarWash.BuildingBlocks.Outbox;

namespace GreenCarWash.PaymentService.Services;

public sealed class NotificationOutboxPublisher : IOutboxEventPublisher
{
    private readonly IHttpClientFactory _clients;

    public NotificationOutboxPublisher(IHttpClientFactory clients)
    {
        _clients = clients;
    }

    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var client = _clients.CreateClient("Notification");
        using var content = new StringContent(outboxEvent.Payload, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync("api/notifications/internal/events", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Notification service returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
    }
}
