namespace GreenCarWash.BookingService.Entities;

public enum ReviewerRole
{
    Customer,
    Washer
}

public sealed class Review
{
    public int ReviewId { get; set; }

    public int BookingId { get; set; }

    public ReviewerRole ReviewerRole { get; set; }

    public int CustomerId { get; set; }

    public int WasherId { get; set; }

    public int Rating { get; set; }

    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; }
}
