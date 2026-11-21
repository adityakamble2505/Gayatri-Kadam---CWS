using System.Collections.Concurrent;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.Extensions.Options;

namespace GreenCarWash.WasherService.Queue;

public sealed class InMemoryWasherQueue : IWasherQueue
{
    private readonly ConcurrentDictionary<int, CityQueue> _cities = new();
    private readonly ConcurrentDictionary<int, int> _washerCity = new();
    private readonly ConcurrentDictionary<int, Offer> _offered = new();
    private readonly IReadOnlyDictionary<string, IWasherSelectionStrategy> _strategies;
    private readonly IClock _clock;
    private readonly QueueOptions _options;

    public InMemoryWasherQueue(
        IEnumerable<IWasherSelectionStrategy> strategies,
        IClock clock,
        IOptions<QueueOptions> options)
    {
        _strategies = strategies.ToDictionary(strategy => strategy.Name, StringComparer.OrdinalIgnoreCase);
        _clock = clock;
        _options = options.Value;
    }

    public void Enqueue(WasherEntry entry)
    {
        if (TryUpdateOffer(entry))
            return;

        var target = City(entry.CityId);
        var fromId = _washerCity.TryGetValue(entry.WasherId, out var existing) ? existing : entry.CityId;
        LockPair(fromId, entry.CityId, () =>
        {
            if (TryUpdateOffer(entry))
                return;

            if (_washerCity.TryGetValue(entry.WasherId, out var cityId) && cityId != entry.CityId)
            {
                var from = City(cityId);
                if (from.Index.Remove(entry.WasherId, out var oldNode))
                {
                    from.Order.Remove(oldNode);
                    entry.EnqueuedAtUtc = oldNode.Value.EnqueuedAtUtc;
                }
            }

            if (target.Index.TryGetValue(entry.WasherId, out var node))
            {
                CopyDetails(node.Value, entry);
                return;
            }

            if (entry.EnqueuedAtUtc == default)
                entry.EnqueuedAtUtc = _clock.UtcNow;
            var added = target.Order.AddLast(entry);
            target.Index[entry.WasherId] = added;
            _washerCity[entry.WasherId] = entry.CityId;
        });
    }

    public void Remove(int washerId)
    {
        _offered.TryRemove(washerId, out _);
        if (!_washerCity.TryGetValue(washerId, out var cityId))
            return;

        var city = City(cityId);
        lock (city.Gate)
        {
            if (city.Index.Remove(washerId, out var node))
                city.Order.Remove(node);
            if (_washerCity.TryGetValue(washerId, out var current) && current == cityId)
                _washerCity.TryRemove(washerId, out _);
        }
    }

    public bool TryDequeueNext(
        int cityId,
        IReadOnlyCollection<int> excludeWasherIds,
        WasherSelectionCriteria criteria,
        out WasherEntry entry)
    {
        entry = null!;
        if (!_cities.TryGetValue(cityId, out var city))
            return false;

        var strategy = Resolve(criteria);
        var excluded = excludeWasherIds.Count == 0 ? null : excludeWasherIds as HashSet<int> ?? excludeWasherIds.ToHashSet();
        lock (city.Gate)
        {
            var candidates = new List<WasherEntry>(city.Order.Count);
            foreach (var washer in city.Order)
            {
                if (excluded is null || !excluded.Contains(washer.WasherId))
                    candidates.Add(washer);
            }

            var picked = strategy.Select(candidates, criteria);
            if (picked is null || !city.Index.Remove(picked.WasherId, out var node))
                return false;

            city.Order.Remove(node);
            _washerCity.TryRemove(picked.WasherId, out _);
            var leaseSeconds = _options.OfferLeaseSeconds > 0 ? _options.OfferLeaseSeconds : 120;
            _offered[picked.WasherId] = new Offer(picked, _clock.UtcNow.AddSeconds(leaseSeconds));
            entry = picked;
            return true;
        }
    }

    public void ReturnToTail(int washerId)
    {
        if (!_offered.TryRemove(washerId, out var offer))
        {
            MoveExistingToTail(washerId);
            return;
        }

        var target = City(offer.Entry.CityId);
        lock (target.Gate)
        {
            if (target.Index.TryGetValue(washerId, out var existing))
            {
                target.Order.Remove(existing);
                target.Order.AddLast(existing);
                return;
            }

            var added = target.Order.AddLast(offer.Entry);
            target.Index[washerId] = added;
            _washerCity[washerId] = offer.Entry.CityId;
        }
    }

    public IReadOnlyList<WasherEntry> Snapshot(int cityId)
    {
        if (!_cities.TryGetValue(cityId, out var city))
            return [];

        lock (city.Gate)
        {
            return city.Order.Select(Copy).ToArray();
        }
    }

    public bool Contains(int washerId) => _washerCity.ContainsKey(washerId);

    public void UpdateDetails(int washerId, decimal avgRating, decimal? latitude, decimal? longitude)
    {
        if (_offered.TryGetValue(washerId, out var offer))
        {
            offer.Entry.AvgRating = avgRating;
            offer.Entry.Latitude = latitude;
            offer.Entry.Longitude = longitude;
        }

        if (!_washerCity.TryGetValue(washerId, out var cityId))
            return;

        var city = City(cityId);
        lock (city.Gate)
        {
            if (!city.Index.TryGetValue(washerId, out var node))
                return;
            node.Value.AvgRating = avgRating;
            node.Value.Latitude = latitude;
            node.Value.Longitude = longitude;
        }
    }

    public void ClearOffer(int washerId) => _offered.TryRemove(washerId, out _);

    public IReadOnlyList<WasherEntry> ExpiredOffers(DateTime utcNow)
    {
        var expired = new List<WasherEntry>();
        foreach (var pair in _offered)
        {
            if (pair.Value.ExpiresAtUtc <= utcNow)
                expired.Add(pair.Value.Entry);
        }

        return expired;
    }

    private void MoveExistingToTail(int washerId)
    {
        if (!_washerCity.TryGetValue(washerId, out var cityId))
            return;

        var city = City(cityId);
        lock (city.Gate)
        {
            if (!city.Index.TryGetValue(washerId, out var node))
                return;
            city.Order.Remove(node);
            city.Order.AddLast(node);
        }
    }

    private IWasherSelectionStrategy Resolve(WasherSelectionCriteria criteria)
    {
        var name = string.IsNullOrWhiteSpace(criteria.Strategy) ? _options.DefaultStrategy : criteria.Strategy.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "Fifo";
        if (_strategies.TryGetValue(name, out var strategy))
            return strategy;
        throw new ArgumentException($"Unknown washer selection strategy '{name}'.");
    }

    private bool TryUpdateOffer(WasherEntry entry)
    {
        if (!_offered.TryGetValue(entry.WasherId, out var offer))
            return false;
        CopyDetails(offer.Entry, entry);
        offer.Entry.CityId = entry.CityId;
        return true;
    }

    private void LockPair(int firstCityId, int secondCityId, Action action)
    {
        if (firstCityId == secondCityId)
        {
            lock (City(firstCityId).Gate)
                action();
            return;
        }

        var lower = Math.Min(firstCityId, secondCityId);
        var higher = Math.Max(firstCityId, secondCityId);
        lock (City(lower).Gate)
        lock (City(higher).Gate)
            action();
    }

    private CityQueue City(int cityId) => _cities.GetOrAdd(cityId, _ => new CityQueue());

    private static void CopyDetails(WasherEntry target, WasherEntry source)
    {
        target.AvgRating = source.AvgRating;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
    }

    private static WasherEntry Copy(WasherEntry source) => new()
    {
        WasherId = source.WasherId,
        CityId = source.CityId,
        AvgRating = source.AvgRating,
        Latitude = source.Latitude,
        Longitude = source.Longitude,
        EnqueuedAtUtc = source.EnqueuedAtUtc
    };

    private sealed class CityQueue
    {
        public object Gate { get; } = new();

        public LinkedList<WasherEntry> Order { get; } = new();

        public Dictionary<int, LinkedListNode<WasherEntry>> Index { get; } = new();
    }

    private sealed class Offer
    {
        public Offer(WasherEntry entry, DateTime expiresAtUtc)
        {
            Entry = entry;
            ExpiresAtUtc = expiresAtUtc;
        }

        public WasherEntry Entry { get; }

        public DateTime ExpiresAtUtc { get; }
    }
}
