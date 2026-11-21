using System.Collections.Concurrent;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GreenCarWash.BookingService.Services;

internal static class SingleTypeOutbox
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static SemaphoreSlim Gate(string eventType) =>
        Gates.GetOrAdd(eventType, _ => new SemaphoreSlim(1, 1));

    public static Task<List<OutboxEvent>> ClaimMemoryAsync(
        BookingDbContext db,
        string eventType,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        db.OutboxEvents
            .Where(evt => evt.EventType == eventType && evt.ProcessedAt == null && evt.NextAttemptAt <= utcNow)
            .OrderBy(evt => evt.EventId)
            .Take(20)
            .ToListAsync(cancellationToken);

    public static async Task<List<OutboxEvent>> ClaimRelationalAsync(
        BookingDbContext db,
        string eventType,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Outbox claim requires a transaction.");
        var ids = new List<long>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                WITH cte AS (
                    SELECT TOP (20) EventId, RetryCount
                    FROM OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedAt IS NULL
                      AND EventType = @eventType
                      AND NextAttemptAt <= SYSUTCDATETIME()
                    ORDER BY EventId
                )
                UPDATE cte
                SET RetryCount = RetryCount
                OUTPUT inserted.EventId
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@eventType";
            parameter.Value = eventType;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetInt64(0));
        }

        if (ids.Count == 0)
            return [];

        return await db.OutboxEvents
            .Where(evt => ids.Contains(evt.EventId))
            .OrderBy(evt => evt.EventId)
            .ToListAsync(cancellationToken);
    }
}
