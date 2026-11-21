using System.Text.Json;
using GreenCarWash.BookingService.Data;
using Microsoft.EntityFrameworkCore;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.BookingService.Services;

public sealed class RefundRetryProcessor
{
    public const string EventType = "RefundPending";

    private readonly BookingDbContext _db;
    private readonly IPaymentCompensation _payments;
    private readonly IClock _clock;
    private readonly ILogger<RefundRetryProcessor> _logger;

    public RefundRetryProcessor(
        BookingDbContext db,
        IPaymentCompensation payments,
        IClock clock,
        ILogger<RefundRetryProcessor> logger)
    {
        _db = db;
        _payments = payments;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            var gate = SingleTypeOutbox.Gate(EventType);
            await gate.WaitAsync(cancellationToken);
            try
            {
                var claimed = await SingleTypeOutbox.ClaimMemoryAsync(_db, EventType, _clock.UtcNow, cancellationToken);
                return await ApplyAsync(claimed, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await SingleTypeOutbox.ClaimRelationalAsync(_db, EventType, cancellationToken);
        var handled = await ApplyAsync(rows, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return handled;
    }

    private async Task<int> ApplyAsync(List<OutboxEvent> claimed, CancellationToken cancellationToken)
    {
        foreach (var evt in claimed)
        {
            var bookingId = ReadBookingId(evt.Payload);
            if (bookingId is not int id)
            {
                evt.ProcessedAt = _clock.UtcNow;
                evt.LastError = "Refund payload has no bookingId.";
                continue;
            }

            bool refunded;
            try
            {
                refunded = await _payments.TryRefundAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                refunded = false;
                _logger.LogWarning(exception, "Refund retry failed for booking {BookingId}", id);
            }

            if (refunded)
            {
                evt.ProcessedAt = _clock.UtcNow;
                evt.LastError = null;
                continue;
            }

            evt.RetryCount += 1;
            evt.LastError = "Refund retry failed.";
            evt.NextAttemptAt = OutboxPublisherBackgroundService<BookingDbContext>.NextAttemptAt(_clock.UtcNow, evt.RetryCount);
            _logger.LogWarning("Refund retry left booking {BookingId} pending", id);
        }

        if (claimed.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }

    private static int? ReadBookingId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (!document.RootElement.TryGetProperty("bookingId", out var value) || value.ValueKind != JsonValueKind.Number)
                return null;
            return value.GetInt32();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class RefundRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RefundRetryWorker> _logger;

    public RefundRetryWorker(IServiceScopeFactory scopes, ILogger<RefundRetryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RefundRetryProcessor>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Refund retry batch failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
