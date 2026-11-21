namespace GreenCarWash.BuildingBlocks.Outbox;

public class OutboxEvent
{
    public long EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
