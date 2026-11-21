namespace GreenCarWash.BookingService.DTOs;

public class CreateBookingDto
{
    public int VehicleId { get; set; }

    public int ServiceId { get; set; }

    public List<int> AddOnIds { get; set; } = [];

    public string? PromoCode { get; set; }

    public string? Instructions { get; set; }
}

public sealed class ScheduleBookingDto : CreateBookingDto
{
    public DateTime BookingDate { get; set; }
}

public sealed class CancelBookingDto
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class AssignWasherDto
{
    public int WasherId { get; set; }
}

public sealed class OrderListQuery
{
    public string Scope { get; set; } = string.Empty;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class BookingAddOnDto
{
    public int AddOnId { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}

public sealed class BookingSummaryDto
{
    public int BookingId { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public string ServiceNameSnapshot { get; init; } = string.Empty;

    public string BookingType { get; init; } = string.Empty;

    public DateTime BookingDate { get; init; }

    public string Status { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public int? WasherId { get; init; }

    public int CityId { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateTime? CancelledAt { get; init; }
}

public sealed class BookingDetailDto
{
    public int BookingId { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    public int VehicleId { get; init; }

    public int ServiceId { get; init; }

    public int? WasherId { get; init; }

    public int CityId { get; init; }

    public string BookingType { get; init; } = string.Empty;

    public DateTime BookingDate { get; init; }

    public int EstimatedDurationMin { get; init; }

    public string Address { get; init; } = string.Empty;

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public string? Instructions { get; init; }

    public string ServiceNameSnapshot { get; init; } = string.Empty;

    public decimal ServicePriceSnapshot { get; init; }

    public int ServiceDiscountPct { get; init; }

    public decimal AddOnsTotal { get; init; }

    public string? PromoCode { get; init; }

    public decimal PromoDiscount { get; init; }

    public decimal TotalAmount { get; init; }

    public int WaterSavedLiters { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? AfterWashPhotoUrl { get; init; }

    public DateTime? AcceptedAt { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateTime? CancelledAt { get; init; }

    public string? CancelledBy { get; init; }

    public string? CancelReason { get; init; }

    public IReadOnlyList<BookingAddOnDto> AddOns { get; init; } = [];
}

public sealed class InternalBookingDto
{
    public int BookingId { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public int CustomerId { get; init; }

    public int CustomerUserId { get; init; }

    public int? WasherId { get; init; }

    public int? WasherUserId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public IReadOnlyList<BookingAddOnDto> AddOns { get; init; } = [];

    public string? AfterWashPhotoUrl { get; init; }

    public string? CancelledBy { get; init; }
}
