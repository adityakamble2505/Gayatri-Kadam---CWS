namespace GreenCarWash.BookingService.Entities;

public sealed class BookingOffer
{
    public long OfferId { get; set; }

    public int BookingId { get; set; }

    public int WasherId { get; set; }

    public DateTime OfferedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public OfferResponse Response { get; set; } = OfferResponse.Pending;

    public DateTime? RespondedAt { get; set; }

    public Booking? Booking { get; set; }
}
