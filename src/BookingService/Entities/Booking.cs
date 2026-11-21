namespace GreenCarWash.BookingService.Entities;

public sealed class Booking
{
    public int BookingId { get; set; }

    public string BookingNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    public int VehicleId { get; set; }

    public int ServiceId { get; set; }

    public int? WasherId { get; set; }

    public int CityId { get; set; }

    public BookingType BookingType { get; set; }

    public DateTime BookingDate { get; set; }

    public int EstimatedDurationMin { get; set; }

    public string Address { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Instructions { get; set; }

    public string ServiceNameSnapshot { get; set; } = string.Empty;

    public decimal ServicePriceSnapshot { get; set; }

    public int ServiceDiscountPct { get; set; }

    public decimal AddOnsTotal { get; set; }

    public string? PromoCode { get; set; }

    public decimal PromoDiscount { get; set; }

    public decimal TotalAmount { get; set; }

    public int WaterSavedLiters { get; set; }

    public BookingStatus Status { get; set; }

    public DateTime? DispatchStartedAt { get; set; }

    public bool NeedsAdminAttention { get; set; }

    public string? AfterWashPhotoUrl { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public CancelledBy? CancelledBy { get; set; }

    public string? CancelReason { get; set; }

    public int RowVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<BookingAddOn> AddOns { get; set; } = new List<BookingAddOn>();
}
