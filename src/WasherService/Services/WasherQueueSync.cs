using GreenCarWash.WasherService.Entities;
using GreenCarWash.WasherService.Queue;

namespace GreenCarWash.WasherService.Services;

internal static class WasherQueueSync
{
    public static bool IsEligible(WasherProfile washer) =>
        washer.IsActive
        && washer.IsAvailable
        && washer.IsQualified
        && washer.AssignmentStatus == AssignmentStatus.Available;

    public static void Apply(IWasherQueue queue, WasherProfile washer)
    {
        if (IsEligible(washer))
        {
            queue.Enqueue(ToEntry(washer));
            return;
        }

        queue.Remove(washer.WasherId);
    }

    public static WasherEntry ToEntry(WasherProfile washer) => new()
    {
        WasherId = washer.WasherId,
        CityId = washer.CityId,
        AvgRating = washer.AvgRating,
        Latitude = washer.Latitude,
        Longitude = washer.Longitude
    };
}
