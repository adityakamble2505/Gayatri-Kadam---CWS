using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Repositories;

namespace GreenCarWash.BookingService.Services;

public interface ILeaderboardService
{
    Task<IReadOnlyList<LeaderboardEntryDto>> TopAsync(int top, CancellationToken cancellationToken);
}

public static class DenseRank
{
    public static IReadOnlyList<(CustomerWashTotal Row, int Rank)> Apply(IEnumerable<CustomerWashTotal> rows)
    {
        var ordered = rows
            .OrderByDescending(row => row.WaterSavedLiters)
            .ThenBy(row => row.CustomerId)
            .ToList();
        var ranked = new List<(CustomerWashTotal Row, int Rank)>(ordered.Count);
        var rank = 0;
        int? previous = null;
        foreach (var row in ordered)
        {
            if (previous != row.WaterSavedLiters)
                rank++;
            previous = row.WaterSavedLiters;
            ranked.Add((row, rank));
        }

        return ranked;
    }
}

public sealed class LeaderboardService : ILeaderboardService
{
    private readonly IBookingInsightsRepository _insights;
    private readonly DisplayNameLookup _names;

    public LeaderboardService(IBookingInsightsRepository insights, DisplayNameLookup names)
    {
        _insights = insights;
        _names = names;
    }

    public async Task<IReadOnlyList<LeaderboardEntryDto>> TopAsync(int top, CancellationToken cancellationToken)
    {
        var limit = top < 1 ? 10 : Math.Min(top, 50);
        var totals = await _insights.CompletedTotalsAsync(cancellationToken);
        var ranked = DenseRank.Apply(totals).Take(limit);
        var entries = new List<LeaderboardEntryDto>();
        foreach (var (row, rank) in ranked)
        {
            entries.Add(new LeaderboardEntryDto
            {
                Rank = rank,
                CustomerId = row.CustomerId,
                DisplayName = await _names.CustomerAsync(row.CustomerId, privacy: true, cancellationToken),
                Washes = row.Washes,
                WaterSavedLiters = row.WaterSavedLiters
            });
        }

        return entries;
    }
}
