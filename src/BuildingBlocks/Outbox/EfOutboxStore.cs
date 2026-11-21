using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BuildingBlocks.Outbox;

public sealed class EfOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly IClock _clock;

    public EfOutboxStore(TDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.UtcNow;
        if (outboxEvent.CreatedAt == default)
            outboxEvent.CreatedAt = now;
        if (outboxEvent.NextAttemptAt == default)
            outboxEvent.NextAttemptAt = now;

        _db.Set<OutboxEvent>().Add(outboxEvent);
        return Task.CompletedTask;
    }
}
