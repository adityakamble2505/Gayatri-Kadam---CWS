using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GreenCarWash.BuildingBlocks.Outbox;

/// <summary>
/// Polls OutboxEvents with the Q13 claim (UPDLOCK, READPAST, ROWLOCK, batch 20).
/// A failed publish waits min(30, 2^RetryCount) minutes. One warning is written per failed batch.
/// </summary>
public sealed class OutboxPublisherBackgroundService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    public const int BatchSize = 20;
    public const int MaxBackoffMinutes = 30;
    public const int PermanentClientErrorAttempts = 3;

    private const int MaxErrorLength = 500;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private const string ClaimSql =
        """
        WITH cte AS (
            SELECT TOP (20) EventId, EventType, Payload, RetryCount
            FROM OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessedAt IS NULL
              AND NextAttemptAt <= SYSUTCDATETIME()
            ORDER BY EventId
        )
        UPDATE cte
        SET RetryCount = RetryCount
        OUTPUT inserted.EventId, inserted.EventType, inserted.Payload, inserted.RetryCount
        """;

    private const string SuccessSql =
        """
        UPDATE OutboxEvents
        SET ProcessedAt = @processedAt
        WHERE EventId = @eventId
          AND ProcessedAt IS NULL
        """;

    private const string FailureSql =
        """
        UPDATE OutboxEvents
        SET RetryCount = RetryCount + 1,
            NextAttemptAt = @nextAttempt,
            LastError = @err
        WHERE EventId = @eventId
          AND ProcessedAt IS NULL
        """;

    private const string PoisonSql =
        """
        UPDATE OutboxEvents
        SET RetryCount = RetryCount + 1,
            ProcessedAt = @processedAt,
            LastError = @err
        WHERE EventId = @eventId
          AND ProcessedAt IS NULL
        """;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<OutboxPublisherBackgroundService<TDbContext>> _logger;
    private readonly HashSet<string> _excluded;

    public OutboxPublisherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<OutboxPublisherBackgroundService<TDbContext>> logger,
        OutboxPublisherOptions<TDbContext>? options = null)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
        _excluded = options?.ExcludedEventTypes ?? [];
    }

    public static DateTime NextAttemptAt(DateTime utcNow, int retryCountAfterIncrement)
    {
        var minutes = Math.Min(MaxBackoffMinutes, Math.Pow(2, retryCountAfterIncrement));
        return utcNow.AddMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Outbox publisher batch failed");
            }

            if (handled == BatchSize)
                continue;

            try
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxEventPublisher>();
        if (!db.Database.IsRelational())
            return await PublishMemoryAsync(db, publisher, cancellationToken);

        return await PublishRelationalAsync(db, publisher, cancellationToken);
    }

    private async Task<int> PublishMemoryAsync(
        TDbContext db,
        IOutboxEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(GateKey(db), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            var pending = await db.Set<OutboxEvent>()
                .Where(evt => evt.ProcessedAt == null && evt.NextAttemptAt <= now)
                .OrderBy(evt => evt.EventId)
                .ToListAsync(cancellationToken);
            var claimed = pending.Where(evt => !_excluded.Contains(evt.EventType)).Take(BatchSize).ToList();

            var retryable = 0;
            var discarded = 0;
            string? firstError = null;
            var stopped = false;
            foreach (var item in claimed)
            {
                try
                {
                    await publisher.PublishAsync(item, cancellationToken);
                    item.ProcessedAt = _clock.UtcNow;
                }
                catch (Exception exception)
                {
                    if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        throw;

                    firstError ??= exception.Message;
                    item.RetryCount += 1;
                    item.LastError = Trim(exception.Message);
                    if (IsClientRejection(exception) && item.RetryCount >= PermanentClientErrorAttempts)
                    {
                        item.ProcessedAt = _clock.UtcNow;
                        discarded++;
                        continue;
                    }

                    item.NextAttemptAt = NextAttemptAt(_clock.UtcNow, item.RetryCount);
                    retryable++;
                    if (IsDeliveryOutage(exception))
                    {
                        stopped = true;
                        break;
                    }
                }
            }

            if (claimed.Count > 0)
                await db.SaveChangesAsync(cancellationToken);
            Warn(retryable, discarded, claimed.Count, firstError, stopped);
            return stopped ? 0 : claimed.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> PublishRelationalAsync(
        TDbContext db,
        IOutboxEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        var claimed = await ClaimAsync(connection, dbTransaction, cancellationToken);

        var retryable = 0;
        var discarded = 0;
        string? firstError = null;
        var stopped = false;
        foreach (var item in claimed)
        {
            try
            {
                await publisher.PublishAsync(item, cancellationToken);
                await ExecuteAsync(
                    connection,
                    dbTransaction,
                    SuccessSql,
                    cancellationToken,
                    ("processedAt", _clock.UtcNow),
                    ("eventId", item.EventId));
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                firstError ??= exception.Message;
                var nextRetry = item.RetryCount + 1;
                var error = Trim(exception.Message);
                if (IsClientRejection(exception) && nextRetry >= PermanentClientErrorAttempts)
                {
                    await ExecuteAsync(
                        connection,
                        dbTransaction,
                        PoisonSql,
                        cancellationToken,
                        ("processedAt", _clock.UtcNow),
                        ("err", error),
                        ("eventId", item.EventId));
                    discarded++;
                    continue;
                }

                await ExecuteAsync(
                    connection,
                    dbTransaction,
                    FailureSql,
                    cancellationToken,
                    ("nextAttempt", NextAttemptAt(_clock.UtcNow, nextRetry)),
                    ("err", error),
                    ("eventId", item.EventId));
                retryable++;
                if (IsDeliveryOutage(exception))
                {
                    stopped = true;
                    break;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        Warn(retryable, discarded, claimed.Count, firstError, stopped);
        return stopped ? 0 : claimed.Count;
    }

    internal static bool IsDeliveryOutage(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
                return true;
            if (current.GetType().Name == "TimeoutRejectedException")
                return true;
            if (current is HttpRequestException http && (http.StatusCode is null || (int)http.StatusCode >= 500))
                return true;
        }

        return false;
    }

    internal static bool IsClientRejection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http && http.StatusCode == HttpStatusCode.BadRequest)
                return true;
        }

        return false;
    }

    private void Warn(int retryable, int discarded, int claimed, string? firstError, bool stopped)
    {
        if (retryable == 0 && discarded == 0)
            return;

        var error = string.IsNullOrWhiteSpace(firstError) ? "publish failed" : firstError;
        if (stopped)
        {
            _logger.LogWarning(
                "Outbox batch stopped after a delivery outage. {Failed} of {Claimed} events were attempted; the rest were left untouched. {Error}",
                retryable,
                claimed,
                error);
            return;
        }

        if (retryable > 0)
        {
            _logger.LogWarning(
                "Outbox batch left {Failed} of {Claimed} events pending and will retry. {Error}",
                retryable,
                claimed,
                error);
        }

        if (discarded > 0)
        {
            _logger.LogWarning(
                "Outbox batch discarded {Discarded} poison message(s) after repeated 400 responses. {Error}",
                discarded,
                error);
        }
    }

    private static string GateKey(DbContext db)
    {
        try
        {
            var database = db.Database.GetDbConnection().Database;
            if (!string.IsNullOrWhiteSpace(database))
                return database;
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            var connection = db.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(connection))
                return connection;
        }
        catch (InvalidOperationException)
        {
        }

        return "in-memory";
    }

    private static string Trim(string? message)
    {
        var error = string.IsNullOrWhiteSpace(message) ? "publish failed" : message;
        return error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    private async Task<List<OutboxEvent>> ClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ClaimText();

        var claimed = new List<OutboxEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new OutboxEvent
            {
                EventId = reader.GetInt64(0),
                EventType = reader.GetString(1),
                Payload = reader.IsDBNull(2) ? "{}" : reader.GetValue(2)?.ToString() ?? "{}",
                RetryCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
            });
        }

        return claimed;
    }

    private string ClaimText()
    {
        if (_excluded.Count == 0)
            return ClaimSql;

        var list = string.Join(", ", _excluded.Select(type => "'" + type.Replace("'", "''", StringComparison.Ordinal) + "'"));
        return ClaimSql.Replace(
            "AND NextAttemptAt <= SYSUTCDATETIME()",
            "AND NextAttemptAt <= SYSUTCDATETIME()\n              AND EventType NOT IN (" + list + ")",
            StringComparison.Ordinal);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
