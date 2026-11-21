namespace GreenCarWash.BookingService.Services;

public enum DispatchKind
{
    Immediate,
    Scheduled
}

public enum OfferDecision
{
    Accepted,
    Declined,
    Expired,
    Cancelled
}

public sealed record DispatchRequest(int BookingId, DispatchKind Kind);

public interface IDispatchQueue
{
    void Enqueue(DispatchRequest request);

    bool TryComplete(int bookingId, int washerId, OfferDecision decision);

    void Cancel(int bookingId);

    bool HoldsOffer(int bookingId, int washerId);
}

public sealed class NullDispatchQueue : IDispatchQueue
{
    public static readonly NullDispatchQueue Instance = new();

    public void Enqueue(DispatchRequest request)
    {
    }

    public bool TryComplete(int bookingId, int washerId, OfferDecision decision) => false;

    public void Cancel(int bookingId)
    {
    }

    public bool HoldsOffer(int bookingId, int washerId) => false;
}
