using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.BookingService.Services;

public static class BookingStateMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.Accepted, BookingStatus.Cancelled],
        [BookingStatus.Scheduled] = [BookingStatus.Accepted, BookingStatus.Cancelled],
        [BookingStatus.Accepted] = [BookingStatus.InProgress, BookingStatus.Cancelled],
        [BookingStatus.InProgress] = [BookingStatus.Completed],
        [BookingStatus.Completed] = [],
        [BookingStatus.Cancelled] = []
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static void Ensure(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
            throw new ConflictException($"Invalid status transition from {from} to {to}.");
    }
}
