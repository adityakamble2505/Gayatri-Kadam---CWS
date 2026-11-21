namespace GreenCarWash.BookingService.Entities;

public sealed class BookingStatusHistory
{
    public long HistoryId { get; set; }

    public int BookingId { get; set; }

    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public int? ChangedByUserId { get; set; }

    public string ChangedByRole { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime ChangedAt { get; set; }

    public Booking? Booking { get; set; }
}
