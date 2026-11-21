using GreenCarWash.BookingService.Entities;

namespace GreenCarWash.BookingService.DTOs;

public sealed class CreateReviewDto
{
    public int BookingId { get; set; }

    public int Rating { get; set; }

    public string? Comments { get; set; }
}

public sealed class ReviewDto
{
    public int ReviewId { get; init; }

    public int BookingId { get; init; }

    public string ReviewerRole { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    public int WasherId { get; init; }

    public int Rating { get; init; }

    public string? Comments { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class LeaderboardEntryDto
{
    public int Rank { get; init; }

    public int CustomerId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int Washes { get; init; }

    public int WaterSavedLiters { get; init; }
}

public sealed class AdminOrderQuery
{
    public string? OrderNumber { get; set; }

    public int? WasherId { get; set; }

    public string? BookingType { get; set; }

    public int? ServiceId { get; set; }

    public string? Status { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class AdminOrderDto
{
    public int BookingId { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    public int? WasherId { get; init; }

    public int ServiceId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public string BookingType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime BookingDate { get; init; }

    public decimal TotalAmount { get; init; }

    public int CityId { get; init; }
}

public sealed class ReportWindow
{
    public DateTime? DateFrom { get; init; }

    public DateTime? DateTo { get; init; }

    public int? CityId { get; init; }
}

public sealed class DailyRevenueDto
{
    public DateTime Day { get; init; }

    public int CompletedCount { get; init; }

    public decimal Revenue { get; init; }

    public decimal RunningTotal { get; init; }
}

public sealed class TopServiceDto
{
    public int CityId { get; init; }

    public int ServiceId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public int BookingCount { get; init; }

    public int Rank { get; init; }
}

public sealed class PeakHourDto
{
    public int DayOfWeek { get; init; }

    public int HourOfDay { get; init; }

    public int BookingCount { get; init; }
}

public sealed class CancellationReportDto
{
    public int CityId { get; init; }

    public int TotalBookings { get; init; }

    public int CancelledCount { get; init; }

    public decimal CancellationRatePct { get; init; }

    public decimal? AvgSecondsToAccept { get; init; }
}

public sealed class AddOnRevenueDto
{
    public int AddOnId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int TimesSold { get; init; }

    public decimal AddOnRevenue { get; init; }
}

public sealed class WasherPerformanceDto
{
    public int WasherId { get; init; }

    public string WasherName { get; init; } = string.Empty;

    public int CompletedCount { get; init; }

    public decimal Revenue { get; init; }

    public decimal? AvgMinutes { get; init; }
}

public static class AdminOrderFilters
{
    public static IQueryable<Booking> Apply(IQueryable<Booking> query, AdminOrderQuery criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.OrderNumber))
        {
            var number = criteria.OrderNumber.Trim();
            query = query.Where(booking => booking.BookingNumber == number);
        }

        if (criteria.WasherId is int washerId)
            query = query.Where(booking => booking.WasherId == washerId);

        if (!string.IsNullOrWhiteSpace(criteria.BookingType))
        {
            if (!Enum.TryParse<BookingType>(criteria.BookingType, true, out var bookingType))
                throw Services.Input.Invalid("BookingType", "Booking type is not recognized.");
            query = query.Where(booking => booking.BookingType == bookingType);
        }

        if (criteria.ServiceId is int serviceId)
            query = query.Where(booking => booking.ServiceId == serviceId);

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            if (!Enum.TryParse<BookingStatus>(criteria.Status, true, out var status))
                throw Services.Input.Invalid("Status", "Status is not recognized.");
            query = query.Where(booking => booking.Status == status);
        }

        if (criteria.DateFrom is DateTime from)
        {
            var start = ClockUtc.AsUtc(from);
            query = query.Where(booking => booking.BookingDate >= start);
        }

        if (criteria.DateTo is DateTime to)
        {
            var end = ClockUtc.AsUtc(to);
            query = query.Where(booking => booking.BookingDate < end);
        }

        return query;
    }
}

public static class ClockUtc
{
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };
}
