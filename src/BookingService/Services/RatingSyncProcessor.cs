using System.Text.Json;
using System.Text.Json.Nodes;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Services;

public sealed class RatingSyncProcessor
{
    public const string EventType = "RatingSyncPending";

    private readonly BookingDbContext _db;
    private readonly IWasherDirectory _washers;
    private readonly IClock _clock;
    private readonly ILogger<RatingSyncProcessor> _logger;

    public RatingSyncProcessor(
        BookingDbContext db,
        IWasherDirectory washers,
        IClock clock,
        ILogger<RatingSyncProcessor> logger)
    {
        _db = db;
        _washers = washers;
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
        var appliedInBatch = new HashSet<int>();
        foreach (var evt in claimed)
        {
            try
            {
                await ApplyOneAsync(evt, appliedInBatch, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                Fail(evt, exception.Message);
                _logger.LogWarning(exception, "Rating sync failed for outbox event {EventId}", evt.EventId);
            }
        }

        if (claimed.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }

    private async Task ApplyOneAsync(OutboxEvent evt, HashSet<int> appliedInBatch, CancellationToken cancellationToken)
    {
        var reviewId = ReadInt(evt.Payload, "reviewId");
        if (reviewId is not int id)
        {
            Discard(evt, "Rating sync payload has no reviewId.");
            return;
        }

        var review = await _db.Reviews.AsNoTracking().FirstOrDefaultAsync(row => row.ReviewId == id, cancellationToken);
        if (review is null || review.ReviewerRole != ReviewerRole.Customer)
        {
            Discard(evt, "Review is not eligible for rating sync.");
            return;
        }

        if (appliedInBatch.Contains(review.ReviewId) || ReadInt(evt.Payload, "appliedReviewId") == review.ReviewId || await AlreadyAppliedAsync(review.ReviewId, evt.EventId, cancellationToken))
        {
            evt.ProcessedAt = _clock.UtcNow;
            evt.LastError = null;
            return;
        }

        await _washers.RateAsync(review.WasherId, review.Rating, cancellationToken);
        evt.Payload = MarkApplied(evt.Payload, review.ReviewId);
        evt.ProcessedAt = _clock.UtcNow;
        evt.LastError = null;
        appliedInBatch.Add(review.ReviewId);
    }

    private async Task<bool> AlreadyAppliedAsync(int reviewId, long eventId, CancellationToken cancellationToken)
    {
        var payloads = await _db.OutboxEvents.AsNoTracking()
            .Where(evt => evt.EventType == EventType && evt.ProcessedAt != null && evt.EventId != eventId)
            .Select(evt => evt.Payload)
            .ToListAsync(cancellationToken);
        return payloads.Any(payload => ReadInt(payload, "appliedReviewId") == reviewId);
    }

    private void Fail(OutboxEvent evt, string error)
    {
        evt.RetryCount += 1;
        evt.LastError = Trim(error);
        evt.NextAttemptAt = OutboxPublisherBackgroundService<BookingDbContext>.NextAttemptAt(_clock.UtcNow, evt.RetryCount);
    }

    private void Discard(OutboxEvent evt, string error)
    {
        evt.ProcessedAt = _clock.UtcNow;
        evt.LastError = Trim(error);
    }

    private static string MarkApplied(string payload, int reviewId)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload)?.AsObject() ?? new JsonObject();
        node["appliedReviewId"] = reviewId;
        return node.ToJsonString();
    }

    private static int? ReadInt(string payload, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (!document.RootElement.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
                return null;
            return value.GetInt32();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string? value)
    {
        var error = string.IsNullOrWhiteSpace(value) ? "Rating sync failed." : value;
        return error.Length <= 500 ? error : error[..500];
    }
}

public sealed class RatingSyncRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RatingSyncRetryWorker> _logger;

    public RatingSyncRetryWorker(IServiceScopeFactory scopes, ILogger<RatingSyncRetryWorker> logger)
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
                await scope.ServiceProvider.GetRequiredService<RatingSyncProcessor>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Rating sync batch failed");
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
