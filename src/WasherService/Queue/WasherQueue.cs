namespace GreenCarWash.WasherService.Queue;

public sealed class QueueOptions
{
    public string DefaultStrategy { get; set; } = "Fifo";

    public int OfferLeaseSeconds { get; set; } = 120;
}

public sealed class WasherEntry
{
    public int WasherId { get; set; }

    public int CityId { get; set; }

    public decimal AvgRating { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public DateTime EnqueuedAtUtc { get; set; }
}

public sealed class WasherSelectionCriteria
{
    public string? Strategy { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }
}

public interface IWasherQueue
{
    void Enqueue(WasherEntry entry);

    void Remove(int washerId);

    bool TryDequeueNext(int cityId, IReadOnlyCollection<int> excludeWasherIds, WasherSelectionCriteria criteria, out WasherEntry entry);

    void ReturnToTail(int washerId);

    IReadOnlyList<WasherEntry> Snapshot(int cityId);

    bool Contains(int washerId);

    void UpdateDetails(int washerId, decimal avgRating, decimal? latitude, decimal? longitude);

    void ClearOffer(int washerId);

    IReadOnlyList<WasherEntry> ExpiredOffers(DateTime utcNow);
}

public interface IWasherSelectionStrategy
{
    string Name { get; }

    WasherEntry? Select(IReadOnlyList<WasherEntry> candidates, WasherSelectionCriteria criteria);
}
