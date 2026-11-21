namespace GreenCarWash.BookingService.Entities;

public sealed class BookingAddOn
{
    public long BookingAddOnId { get; set; }

    public int BookingId { get; set; }

    public int AddOnId { get; set; }

    public string NameSnapshot { get; set; } = string.Empty;

    public decimal PriceSnapshot { get; set; }

    public Booking? Booking { get; set; }
}
