using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Repositories;

public interface IReviewRepository
{
    Task<Booking?> FindBookingAsync(int bookingId, CancellationToken cancellationToken);

    Task AddAsync(Review review, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    Task AddOutboxAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Review> Items, int Total)> ListForWasherAsync(
        int washerId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}

public sealed class ReviewRepository : IReviewRepository
{
    private readonly BookingDbContext _db;

    public ReviewRepository(BookingDbContext db)
    {
        _db = db;
    }

    public Task<Booking?> FindBookingAsync(int bookingId, CancellationToken cancellationToken) =>
        _db.Bookings.AsNoTracking().FirstOrDefaultAsync(booking => booking.BookingId == bookingId, cancellationToken);

    public async Task AddAsync(Review review, CancellationToken cancellationToken)
    {
        var exists = await _db.Reviews.AnyAsync(
            row => row.BookingId == review.BookingId && row.ReviewerRole == review.ReviewerRole,
            cancellationToken);
        if (exists)
            throw new ConflictException("A review already exists for this booking.");

        await _db.Reviews.AddAsync(review, cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A review already exists for this booking.");
        }
    }

    public async Task AddOutboxAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        _db.OutboxEvents.Add(outboxEvent);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Review> Items, int Total)> ListForWasherAsync(
        int washerId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Reviews.AsNoTracking()
            .Where(review => review.WasherId == washerId && review.ReviewerRole == ReviewerRole.Customer);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.ReviewId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
