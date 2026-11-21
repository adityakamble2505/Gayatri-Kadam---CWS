using System.Collections.Concurrent;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Repositories;

public enum AcceptOutcome
{
    Accepted,
    AlreadyAssigned,
    Overlap,
    NotOffered,
    OfferedToOther
}

public readonly record struct AcceptAttempt(AcceptOutcome Outcome, BookingStatus PreviousStatus);

public interface IBookingRepository
{
    Task<Booking> CommitNewAsync(Booking booking, int changedByUserId, CancellationToken cancellationToken);

    Task TouchAsync(int bookingId, DateTime utcNow, CancellationToken cancellationToken);

    Task<Booking?> FindAsync(int bookingId, CancellationToken cancellationToken);

    Task<Booking?> FindReadAsync(int bookingId, CancellationToken cancellationToken);

    Task<AcceptAttempt> TryAcceptAsync(
        int bookingId,
        int washerId,
        int washerCityId,
        int changedByUserId,
        DateTime utcNow,
        CancellationToken cancellationToken,
        bool requireOffer = true,
        bool clearAttention = false,
        string role = "Washer",
        string note = "Washer accepted");

    Task RevertAcceptAsync(int bookingId, int washerId, BookingStatus previousStatus, DateTime utcNow, CancellationToken cancellationToken);

    Task ChangeStatusAsync(
        Booking booking,
        BookingStatus to,
        int? changedByUserId,
        string role,
        string? note,
        string? outboxEventType,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task AddNoteAsync(int bookingId, int changedByUserId, string role, string note, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> HasOverlapAsync(int washerId, DateTime newStart, DateTime newEnd, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Booking> Items, int Total)> ListAsync(
        int? customerId,
        int? washerId,
        IReadOnlyCollection<BookingStatus> statuses,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<BookingOffer> AddOfferAsync(BookingOffer offer, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> OfferedWasherIdsAsync(int bookingId, CancellationToken cancellationToken);

    Task<int> CountOffersAsync(int bookingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookingOffer>> LiveOffersAsync(int bookingId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> ExpireOfferIfPendingAsync(long offerId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> DeclineOfferAsync(int bookingId, int washerId, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ExpirePendingOffersAsync(int bookingId, DateTime utcNow, int? exceptWasherId, CancellationToken cancellationToken);

    Task<bool> FlagAdminAsync(int bookingId, string reason, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ClaimScheduledAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> EscalationsDueAsync(DateTime utcNow, int windowMinutes, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> RemindersDueAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> HasReminderAsync(int bookingId, CancellationToken cancellationToken);

    Task AppendEventAsync(int bookingId, string eventType, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookingOffer>> PendingOffersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> PendingImmediateAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ScheduledInDispatchAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> AttentionAsync(CancellationToken cancellationToken);
}

public sealed partial class BookingRepository : IBookingRepository
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> WasherGates = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> BookingGates = new();

    private readonly BookingDbContext _db;
    private readonly IBookingOutbox _outbox;

    public BookingRepository(BookingDbContext db, IBookingOutbox outbox)
    {
        _db = db;
        _outbox = outbox;
    }

    public async Task<Booking> CommitNewAsync(Booking booking, int changedByUserId, CancellationToken cancellationToken)
    {
        async Task SaveGraphAsync()
        {
            booking.BookingNumber = "N" + Guid.NewGuid().ToString("N")[..19];
            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync(cancellationToken);

            booking.BookingNumber = $"GCW-{booking.CreatedAt:yyyyMMdd}-{booking.BookingId:D6}";
            if (booking.BookingNumber.Length > 20)
                throw new InvalidOperationException("Booking number exceeds 20 characters.");

            _db.BookingStatusHistory.Add(new BookingStatusHistory
            {
                BookingId = booking.BookingId,
                FromStatus = null,
                ToStatus = booking.Status.ToString(),
                ChangedByUserId = changedByUserId,
                ChangedByRole = "Customer",
                Note = "Booking created",
                ChangedAt = booking.CreatedAt
            });
            _db.OutboxEvents.Add(await _outbox.CreateAsync(
                booking,
                "BookingCreated",
                booking.Status.ToString(),
                booking.CreatedAt,
                cancellationToken));
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (_db.Database.IsRelational())
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await SaveGraphAsync();
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await SaveGraphAsync();
        }

        return booking;
    }

    public async Task TouchAsync(int bookingId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.FirstAsync(row => row.BookingId == bookingId, cancellationToken);
        booking.RowVersion += 1;
        booking.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<Booking?> FindAsync(int bookingId, CancellationToken cancellationToken) =>
        _db.Bookings.Include(booking => booking.AddOns)
            .FirstOrDefaultAsync(booking => booking.BookingId == bookingId, cancellationToken);

    public Task<Booking?> FindReadAsync(int bookingId, CancellationToken cancellationToken) =>
        _db.Bookings.AsNoTracking().Include(booking => booking.AddOns)
            .FirstOrDefaultAsync(booking => booking.BookingId == bookingId, cancellationToken);

    public async Task<AcceptAttempt> TryAcceptAsync(
        int bookingId,
        int washerId,
        int washerCityId,
        int changedByUserId,
        DateTime utcNow,
        CancellationToken cancellationToken,
        bool requireOffer = true,
        bool clearAttention = false,
        string role = "Washer",
        string note = "Washer accepted")
    {
        if (_db.Database.IsRelational())
            return await TryAcceptRelationalAsync(
                bookingId, washerId, washerCityId, changedByUserId, utcNow, requireOffer, clearAttention, role, note, cancellationToken);

        var washerGate = WasherGates.GetOrAdd(washerId, _ => new SemaphoreSlim(1, 1));
        var bookingGate = BookingGates.GetOrAdd(bookingId, _ => new SemaphoreSlim(1, 1));
        await washerGate.WaitAsync(cancellationToken);
        try
        {
            await bookingGate.WaitAsync(cancellationToken);
            try
            {
                var booking = await _db.Bookings.FirstOrDefaultAsync(row => row.BookingId == bookingId, cancellationToken);
                if (booking is null)
                    throw new NotFoundException("Booking", bookingId);
                if (booking.CityId != washerCityId)
                    throw new ConflictException("Washer is not in the booking city.");

                var from = booking.Status;
                if (booking.WasherId is not null || booking.Status is not (BookingStatus.Pending or BookingStatus.Scheduled))
                    return new AcceptAttempt(AcceptOutcome.AlreadyAssigned, from);

                var blocked = await OfferGateAsync(booking, washerId, requireOffer, cancellationToken);
                if (blocked is AcceptOutcome offerBlock)
                    return new AcceptAttempt(offerBlock, from);

                var newEnd = booking.BookingDate.AddMinutes(booking.EstimatedDurationMin + 30);
                if (await HasOverlapAsync(washerId, booking.BookingDate, newEnd, cancellationToken))
                    return new AcceptAttempt(AcceptOutcome.Overlap, from);

                booking.WasherId = washerId;
                booking.Status = BookingStatus.Accepted;
                booking.AcceptedAt = utcNow;
                if (clearAttention)
                    booking.NeedsAdminAttention = false;
                booking.RowVersion += 1;
                booking.UpdatedAt = utcNow;
                await MarkWinningOfferAsync(booking.BookingId, washerId, utcNow, cancellationToken);
                AddHistory(booking.BookingId, from, BookingStatus.Accepted, changedByUserId, role, note, utcNow);
                _db.OutboxEvents.Add(await _outbox.CreateAsync(
                    booking,
                    "WasherAccepted",
                    nameof(BookingStatus.Accepted),
                    utcNow,
                    cancellationToken,
                    washerId));
                await _db.SaveChangesAsync(cancellationToken);
                return new AcceptAttempt(AcceptOutcome.Accepted, from);
            }
            finally
            {
                bookingGate.Release();
            }
        }
        finally
        {
            washerGate.Release();
        }
    }

    public async Task RevertAcceptAsync(
        int bookingId,
        int washerId,
        BookingStatus previousStatus,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(row => row.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        if (booking.WasherId != washerId || booking.Status != BookingStatus.Accepted)
            return;

        booking.WasherId = null;
        booking.Status = previousStatus;
        booking.AcceptedAt = null;
        booking.RowVersion += 1;
        booking.UpdatedAt = utcNow;
        var offer = await _db.BookingOffers.FirstOrDefaultAsync(
            row => row.BookingId == bookingId && row.WasherId == washerId && row.Response == OfferResponse.Accepted,
            cancellationToken);
        if (offer is not null)
        {
            offer.Response = OfferResponse.Pending;
            offer.RespondedAt = null;
        }

        AddHistory(bookingId, BookingStatus.Accepted, previousStatus, SystemActor.SystemUserId, "System", "Washer confirm failed", utcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Booking booking,
        BookingStatus to,
        int? changedByUserId,
        string role,
        string? note,
        string? outboxEventType,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        async Task ApplyAsync()
        {
            var from = booking.Status;
            booking.Status = to;
            booking.RowVersion += 1;
            booking.UpdatedAt = utcNow;
            AddHistory(booking.BookingId, from, to, changedByUserId, role, note, utcNow);
            if (!string.IsNullOrWhiteSpace(outboxEventType))
            {
                var reason = outboxEventType == "BookingCancelled" ? note : null;
                _db.OutboxEvents.Add(await _outbox.CreateAsync(
                    booking,
                    outboxEventType,
                    to.ToString(),
                    utcNow,
                    cancellationToken,
                    reason: reason));
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        if (_db.Database.IsRelational())
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await ApplyAsync();
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await ApplyAsync();
    }

    public async Task AddNoteAsync(int bookingId, int changedByUserId, string role, string note, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (!await _db.Bookings.AnyAsync(row => row.BookingId == bookingId, cancellationToken))
            throw new NotFoundException("Booking", bookingId);

        var status = await _db.Bookings.Where(row => row.BookingId == bookingId)
            .Select(row => row.Status)
            .FirstAsync(cancellationToken);
        AddHistory(bookingId, status, status, changedByUserId, role, note, utcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasOverlapAsync(int washerId, DateTime newStart, DateTime newEnd, CancellationToken cancellationToken) =>
        _db.Bookings.AnyAsync(booking =>
            booking.WasherId == washerId
            && (booking.Status == BookingStatus.Accepted || booking.Status == BookingStatus.InProgress)
            && booking.BookingDate < newEnd
            && booking.BookingDate.AddMinutes(booking.EstimatedDurationMin + 30) > newStart,
            cancellationToken);

    public async Task<(IReadOnlyList<Booking> Items, int Total)> ListAsync(
        int? customerId,
        int? washerId,
        IReadOnlyCollection<BookingStatus> statuses,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Bookings.AsNoTracking().Where(booking => statuses.Contains(booking.Status));
        if (customerId is int customer)
            query = query.Where(booking => booking.CustomerId == customer);
        if (washerId is int washer)
            query = query.Where(booking => booking.WasherId == washer);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(booking => booking.BookingDate)
            .ThenByDescending(booking => booking.BookingId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    private async Task<AcceptAttempt> TryAcceptRelationalAsync(
        int bookingId,
        int washerId,
        int washerCityId,
        int changedByUserId,
        DateTime utcNow,
        bool requireOffer,
        bool clearAttention,
        string role,
        string note,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var booking = await _db.Bookings.FirstOrDefaultAsync(row => row.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        if (booking.CityId != washerCityId)
            throw new ConflictException("Washer is not in the booking city.");

        var from = booking.Status;
        if (booking.WasherId is not null || booking.Status is not (BookingStatus.Pending or BookingStatus.Scheduled))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AcceptAttempt(AcceptOutcome.AlreadyAssigned, from);
        }

        var blocked = await OfferGateAsync(booking, washerId, requireOffer, cancellationToken);
        if (blocked is AcceptOutcome offerBlock)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AcceptAttempt(offerBlock, from);
        }

        var newEnd = booking.BookingDate.AddMinutes(booking.EstimatedDurationMin + 30);
        if (await HasOverlapAsync(washerId, booking.BookingDate, newEnd, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AcceptAttempt(AcceptOutcome.Overlap, from);
        }

        _db.Entry(booking).State = EntityState.Detached;
        var rows = await ExecuteHotAsync(
            $"""
            UPDATE Bookings
            SET WasherId = {washerId},
                Status = 'Accepted',
                AcceptedAt = {utcNow},
                UpdatedAt = {utcNow},
                RowVersion = RowVersion + 1
            WHERE BookingId = {bookingId}
              AND WasherId IS NULL
              AND Status IN ('Pending', 'Scheduled')
            """,
            cancellationToken);
        if (rows != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AcceptAttempt(AcceptOutcome.AlreadyAssigned, from);
        }

        await MarkWinningOfferAsync(bookingId, washerId, utcNow, cancellationToken);
        if (clearAttention)
        {
            await ExecuteHotAsync(
                $"UPDATE Bookings SET NeedsAdminAttention = 0, UpdatedAt = {utcNow} WHERE BookingId = {bookingId}",
                cancellationToken);
        }

        AddHistory(bookingId, from, BookingStatus.Accepted, changedByUserId, role, note, utcNow);
        _db.OutboxEvents.Add(await _outbox.CreateAsync(
            booking,
            "WasherAccepted",
            nameof(BookingStatus.Accepted),
            utcNow,
            cancellationToken,
            washerId));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AcceptAttempt(AcceptOutcome.Accepted, from);
    }

    private async Task<int> ExecuteHotAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        var rows = 0;
        await SqlServerErrors.ExecuteOnceAsync(async () =>
        {
            rows = await _db.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
        });
        return rows;
    }

    private void AddHistory(
        int bookingId,
        BookingStatus? from,
        BookingStatus to,
        int? userId,
        string role,
        string? note,
        DateTime utcNow)
    {
        _db.BookingStatusHistory.Add(new BookingStatusHistory
        {
            BookingId = bookingId,
            FromStatus = from?.ToString(),
            ToStatus = to.ToString(),
            ChangedByUserId = userId,
            ChangedByRole = role,
            Note = note,
            ChangedAt = utcNow
        });
    }
}
