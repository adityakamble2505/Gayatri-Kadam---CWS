# Washing queue

The washing queue is not stored in the database. `BookingOffers` is an audit log of offers that were made. It is not the queue, and nothing in the dispatch path reconstructs order from that table.

`WasherService` owns the queue. It keeps one queue per city in process memory, behind the `IWasherQueue` interface. Each city has a `LinkedList` (the order washers are offered work) and a `Dictionary` keyed by washer id (the current node, so a remove or a move is O(1)). Every read and write of a city's list and dictionary takes that city's lock.

## Strategies

`IWasherQueue` selects the next washer with one of three strategies:

- **Fifo** — the washer at the head of the list.
- **TopRated** — the available washer in that city with the highest `AvgRating`.
- **Nearest** — the available washer in that city with the shortest distance to the booking's latitude and longitude. Washers with a null location are skipped by this strategy.

A washer who is not active, not available, not qualified, or whose `AssignmentStatus` is not `Available` is not in the list.

## Startup

On process start, `WasherService` runs Q10 against WasherDB:

```sql
SELECT WasherId, CityId, AvgRating, Latitude, Longitude
FROM WasherProfiles
WHERE IsActive = 1
  AND IsAvailable = 1
  AND IsQualified = 1
  AND AssignmentStatus = 'Available';
```

Those rows are grouped by `CityId` and inserted into that city's list. Until this load finishes, the dispatcher does not offer work.

## Dispatch

`BookingService` runs a `DispatchCoordinator`, a `BackgroundService` that reads a `Channel<DispatchRequest>`.

For one booking the coordinator:

1. Asks `WasherService` for a single washer (one strategy, one city).
2. Offers that booking to that washer only, with a 60-second window.
3. Writes a `BookingOffers` row (`Response = 'Pending'`, `ExpiresAt` 60 seconds ahead) as audit. The offer row does not decide who is next.
4. Waits for accept, decline, or timeout.

Accept uses the race-safe update in Q1 (`WasherId IS NULL` and status still `Pending` or `Scheduled`). The winner is the update whose `ROW_COUNT()` is 1.

Decline, or no answer within 60 seconds, sends that washer to the tail of their city list and records `Declined` or `Expired` on the offer row. The coordinator then offers the booking to the next washer.

After 5 attempts with no accept, the coordinator sets `Bookings.NeedsAdminAttention = 1` and stops offering that booking.

## Restart

A restart clears the in-memory lists. Recovery is:

1. Rebuild every city queue from Q10.
2. Leave `BookingOffers` as history. Do not replay it into the list.
3. Find BookingDB rows that still need a washer (`WasherId IS NULL`, status `Pending` or `Scheduled`, and scheduled rows whose `DispatchStartedAt` is already set). Enqueue a new `DispatchRequest` for each.
4. A washer who was mid-offer is simply eligible again if Q10 still returns them. The 60-second offer does not survive the restart; the new process offers again, and Q1 still prevents two accepts.

## Limit

This queue lives in one process. A second API instance would have its own lists and could offer the same booking twice. Q1 still lets only one accept win, but the offer order would be wrong. When more than one instance is required, replace `IWasherQueue` with a Redis implementation of the same interface. `BookingOffers` stays an audit log either way.
