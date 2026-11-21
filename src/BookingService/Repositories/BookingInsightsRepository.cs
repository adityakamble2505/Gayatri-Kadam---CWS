using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Repositories;

public sealed record CustomerWashTotal(int CustomerId, int Washes, int WaterSavedLiters);

public interface IBookingInsightsRepository
{
    Task<IReadOnlyList<CustomerWashTotal>> CompletedTotalsAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<Booking> Items, int Total)> OrdersAsync(AdminOrderQuery criteria, int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ExportOrdersAsync(AdminOrderQuery criteria, int take, CancellationToken cancellationToken);
}

public sealed class BookingInsightsRepository : IBookingInsightsRepository
{
    private readonly BookingDbContext _db;

    public BookingInsightsRepository(BookingDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerWashTotal>> CompletedTotalsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Bookings.AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Completed)
            .GroupBy(booking => booking.CustomerId)
            .Select(group => new CustomerWashTotal(
                group.Key,
                group.Count(),
                group.Sum(booking => booking.WaterSavedLiters)))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<(IReadOnlyList<Booking> Items, int Total)> OrdersAsync(
        AdminOrderQuery criteria,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = AdminOrderFilters.Apply(_db.Bookings.AsNoTracking(), criteria);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(booking => booking.BookingDate)
            .ThenByDescending(booking => booking.BookingId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<Booking>> ExportOrdersAsync(
        AdminOrderQuery criteria,
        int take,
        CancellationToken cancellationToken)
    {
        var query = AdminOrderFilters.Apply(_db.Bookings.AsNoTracking(), criteria);
        return await query
            .OrderByDescending(booking => booking.BookingDate)
            .ThenByDescending(booking => booking.BookingId)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
