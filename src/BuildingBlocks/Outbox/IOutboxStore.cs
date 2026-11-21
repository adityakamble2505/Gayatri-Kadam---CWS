namespace GreenCarWash.BuildingBlocks.Outbox;

public interface IOutboxStore
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}

public interface IOutboxEventPublisher
{
    Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
