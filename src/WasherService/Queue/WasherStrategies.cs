namespace GreenCarWash.WasherService.Queue;

public static class Geo
{
    private const double EarthRadiusMiles = 6370986d / 1609.344d;

    public static double Miles(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var dLat = DegreesToRadians(latitude2 - latitude1);
        var dLon = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMiles * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);
}

public sealed class FifoStrategy : IWasherSelectionStrategy
{
    public string Name => "Fifo";

    public WasherEntry? Select(IReadOnlyList<WasherEntry> candidates, WasherSelectionCriteria criteria) =>
        candidates.Count == 0 ? null : candidates[0];
}

public sealed class TopRatedStrategy : IWasherSelectionStrategy
{
    public string Name => "TopRated";

    public WasherEntry? Select(IReadOnlyList<WasherEntry> candidates, WasherSelectionCriteria criteria) =>
        candidates
            .OrderByDescending(washer => washer.AvgRating)
            .ThenBy(washer => washer.EnqueuedAtUtc)
            .FirstOrDefault();
}

public sealed class NearestStrategy : IWasherSelectionStrategy
{
    public string Name => "Nearest";

    public WasherEntry? Select(IReadOnlyList<WasherEntry> candidates, WasherSelectionCriteria criteria)
    {
        var located = new List<(WasherEntry Washer, double Miles)>();
        var missing = new List<WasherEntry>();
        foreach (var washer in candidates)
        {
            if (washer.Latitude is decimal lat
                && washer.Longitude is decimal lng
                && criteria.Latitude is decimal originLat
                && criteria.Longitude is decimal originLng)
            {
                located.Add((washer, Geo.Miles((double)originLat, (double)originLng, (double)lat, (double)lng)));
            }
            else
            {
                missing.Add(washer);
            }
        }

        var ordered = located
            .OrderBy(pair => pair.Miles)
            .ThenBy(pair => pair.Washer.EnqueuedAtUtc)
            .Select(pair => pair.Washer)
            .Concat(missing.OrderBy(washer => washer.EnqueuedAtUtc));
        return ordered.FirstOrDefault();
    }
}
