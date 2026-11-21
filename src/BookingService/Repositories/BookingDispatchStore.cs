using System.Data;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GreenCarWash.BookingService.Repositories;

public sealed partial class BookingRepository
{
    private static readonly SemaphoreSlim ClaimGate = new(1, 1);

    public async Task<BookingOffer> AddOfferAsync(BookingOffer offer, DateTime utcNow, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(row => row.BookingId == offer.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", offer.BookingId);
        _db.BookingOffers.Add(offer);
        _db.OutboxEvents.Add(await _outbox.CreateAsync(
            booking,
            "WashRequest",
            booking.Status.ToString(),
            utcNow,
            cancellationToken,
            offer.WasherId));
        await _db.SaveChangesAsync(cancellationToken);
        return offer;
    }

    public async Task<IReadOnlyList<int>> OfferedWasherIdsAsync(int bookingId, CancellationToken cancellationToken) =>
        await _db.BookingOffers.AsNoTracking()
            .Where(offer => offer.BookingId == bookingId)
            .Select(offer => offer.WasherId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<int> CountOffersAsync(int bookingId, CancellationToken cancellationToken) =>
        _db.BookingOffers.CountAsync(offer => offer.BookingId == bookingId, cancellationToken);

    public async Task<IReadOnlyList<BookingOffer>> LiveOffersAsync(int bookingId, DateTime utcNow, CancellationToken cancellationToken) =>
        await _db.BookingOffers.AsNoTracking()
            .Where(offer => offer.BookingId == bookingId && offer.Response == OfferResponse.Pending && offer.ExpiresAt > utcNow)
            .OrderBy(offer => offer.OfferId)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExpireOfferIfPendingAsync(long offerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var offer = await _db.BookingOffers.FirstOrDefaultAsync(row => row.OfferId == offerId, cancellationToken);
        if (offer is null || offer.Response != OfferResponse.Pending)
            return false;
        offer.Response = OfferResponse.Expired;
        offer.RespondedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeclineOfferAsync(int bookingId, int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var offer = await _db.BookingOffers
            .Where(row => row.BookingId == bookingId && row.WasherId == washerId && row.Response == OfferResponse.Pending)
            .OrderByDescending(row => row.OfferId)
            .FirstOrDefaultAsync(cancellationToken);
        if (offer is null)
            return false;
        offer.Response = OfferResponse.Declined;
        offer.RespondedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<int>> ExpirePendingOffersAsync(
        int bookingId,
        DateTime utcNow,
        int? exceptWasherId,
        CancellationToken cancellationToken)
    {
        var offers = await _db.BookingOffers
            .Where(offer => offer.BookingId == bookingId && offer.Response == OfferResponse.Pending)
            .ToListAsync(cancellationToken);
        var released = new List<int>();
        foreach (var offer in offers)
        {
            if (exceptWasherId is int winner && offer.WasherId == winner)
                continue;
            offer.Response = OfferResponse.Expired;
            offer.RespondedAt = utcNow;
            released.Add(offer.WasherId);
        }

        if (released.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return released;
    }

    public async Task<bool> FlagAdminAsync(int bookingId, string reason, DateTime utcNow, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(row => row.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.WasherId is not null)
            return false;
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Scheduled))
            return false;
        if (booking.NeedsAdminAttention)
            return false;

        booking.NeedsAdminAttention = true;
        booking.RowVersion += 1;
        booking.UpdatedAt = utcNow;
        _db.OutboxEvents.Add(await _outbox.CreateAsync(
            booking,
            "AdminAlert",
            booking.Status.ToString(),
            utcNow,
            cancellationToken,
            reason: reason));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Booking>> ClaimScheduledAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var deadline = utcNow.AddHours(2);
        if (_db.Database.IsRelational())
            return await ClaimScheduledRelationalAsync(utcNow, deadline, cancellationToken);

        await ClaimGate.WaitAsync(cancellationToken);
        try
        {
            var due = await _db.Bookings
                .Where(booking => booking.Status == BookingStatus.Scheduled
                    && booking.WasherId == null
                    && booking.DispatchStartedAt == null
                    && booking.BookingDate <= deadline)
                .OrderBy(booking => booking.BookingDate)
                .Take(50)
                .ToListAsync(cancellationToken);
            foreach (var booking in due)
            {
                booking.DispatchStartedAt = utcNow;
                booking.UpdatedAt = utcNow;
            }

            if (due.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);
            return due;
        }
        finally
        {
            ClaimGate.Release();
        }
    }

    public Task<IReadOnlyList<Booking>> EscalationsDueAsync(DateTime utcNow, int windowMinutes, CancellationToken cancellationToken)
    {
        var cutoff = utcNow.AddMinutes(-windowMinutes);
        return Load(query => query.Where(booking =>
            booking.Status == BookingStatus.Scheduled
            && booking.WasherId == null
            && booking.DispatchStartedAt != null
            && booking.DispatchStartedAt <= cutoff), cancellationToken);
    }

    public Task<IReadOnlyList<Booking>> RemindersDueAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var from = utcNow.AddMinutes(115);
        var to = utcNow.AddMinutes(125);
        return Load(query => query.Where(booking =>
            booking.Status == BookingStatus.Accepted
            && booking.BookingType == BookingType.Scheduled
            && booking.BookingDate >= from
            && booking.BookingDate <= to), cancellationToken);
    }

    public Task<bool> HasReminderAsync(int bookingId, CancellationToken cancellationToken)
    {
        var marker = $"\"bookingId\":{bookingId}";
        return _db.OutboxEvents.AnyAsync(
            evt => evt.EventType == "Reminder2h" && evt.Payload.Contains(marker),
            cancellationToken);
    }

    public async Task AppendEventAsync(int bookingId, string eventType, DateTime utcNow, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(row => row.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        _db.OutboxEvents.Add(await _outbox.CreateAsync(
            booking,
            eventType,
            booking.Status.ToString(),
            utcNow,
            cancellationToken));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BookingOffer>> PendingOffersAsync(CancellationToken cancellationToken) =>
        await _db.BookingOffers.AsNoTracking()
            .Where(offer => offer.Response == OfferResponse.Pending)
            .ToListAsync(cancellationToken);

    public Task<IReadOnlyList<Booking>> PendingImmediateAsync(CancellationToken cancellationToken) =>
        Load(query => query.Where(booking =>
            booking.Status == BookingStatus.Pending
            && booking.BookingType == BookingType.Immediate
            && booking.WasherId == null
            && !booking.NeedsAdminAttention), cancellationToken);

    public Task<IReadOnlyList<Booking>> ScheduledInDispatchAsync(CancellationToken cancellationToken) =>
        Load(query => query.Where(booking =>
            booking.Status == BookingStatus.Scheduled
            && booking.WasherId == null
            && booking.DispatchStartedAt != null
            && !booking.NeedsAdminAttention), cancellationToken);

    public async Task<IReadOnlyList<Booking>> AttentionAsync(CancellationToken cancellationToken) =>
        await _db.Bookings.AsNoTracking()
            .Where(booking => booking.NeedsAdminAttention)
            .OrderBy(booking => booking.BookingDate)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<Booking>> Load(
        Func<IQueryable<Booking>, IQueryable<Booking>> shape,
        CancellationToken cancellationToken) =>
        await shape(_db.Bookings).ToListAsync(cancellationToken);

    private async Task<AcceptOutcome?> OfferGateAsync(Booking booking, int washerId, bool requireOffer, CancellationToken cancellationToken)
    {
        if (!requireOffer || booking.BookingType != BookingType.Immediate)
            return null;

        var pending = await _db.BookingOffers.AsNoTracking()
            .Where(offer => offer.BookingId == booking.BookingId && offer.Response == OfferResponse.Pending)
            .Select(offer => offer.WasherId)
            .ToListAsync(cancellationToken);
        if (pending.Contains(washerId))
            return null;
        return pending.Count == 0 ? AcceptOutcome.NotOffered : AcceptOutcome.OfferedToOther;
    }

    private async Task MarkWinningOfferAsync(int bookingId, int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var offer = await _db.BookingOffers.FirstOrDefaultAsync(
            row => row.BookingId == bookingId && row.WasherId == washerId && row.Response == OfferResponse.Pending,
            cancellationToken);
        if (offer is null)
            return;
        offer.Response = OfferResponse.Accepted;
        offer.RespondedAt = utcNow;
    }

    private async Task<IReadOnlyList<Booking>> ClaimScheduledRelationalAsync(
        DateTime utcNow,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            WITH cte AS (
                SELECT TOP (50) BookingId, DispatchStartedAt, UpdatedAt
                FROM Bookings WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = N'Scheduled'
                  AND WasherId IS NULL
                  AND DispatchStartedAt IS NULL
                  AND BookingDate <= @deadline
                ORDER BY BookingDate
            )
            UPDATE cte
            SET DispatchStartedAt = @utcNow,
                UpdatedAt = @utcNow
            OUTPUT inserted.BookingId
            """;
        var deadlineParameter = command.CreateParameter();
        deadlineParameter.ParameterName = "@deadline";
        deadlineParameter.Value = deadline;
        command.Parameters.Add(deadlineParameter);
        var nowParameter = command.CreateParameter();
        nowParameter.ParameterName = "@utcNow";
        nowParameter.Value = utcNow;
        command.Parameters.Add(nowParameter);

        var ids = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetInt32(0));
        }

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var claimed = await _db.Bookings.Where(booking => ids.Contains(booking.BookingId)).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed.OrderBy(booking => booking.BookingDate).ToList();
    }
}
