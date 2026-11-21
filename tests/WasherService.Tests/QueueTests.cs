using System.Collections.Concurrent;
using FluentAssertions;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.WasherService.Queue;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace GreenCarWash.WasherService.Tests;

public sealed class QueueTests
{
    [Test]
    public async Task ConcurrentDequeue_ReturnsEachWasherOnce()
    {
        var queue = CreateQueue();
        for (var id = 1; id <= 20; id++)
            queue.Enqueue(Entry(id, rating: 4m));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winners = new ConcurrentBag<int>();
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            if (queue.TryDequeueNext(1, [], new WasherSelectionCriteria { Strategy = "Fifo" }, out var entry))
                winners.Add(entry.WasherId);
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        winners.Should().HaveCount(20);
        winners.Distinct().Should().HaveCount(20);
        queue.Snapshot(1).Should().BeEmpty();
    }

    [Test]
    public async Task ConcurrentEnqueueAndRemove_StaysConsistent()
    {
        var queue = CreateQueue();
        var errors = new ConcurrentQueue<Exception>();
        var tasks = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                try
                {
                    var id = ((worker * 500) + i) % 40 + 1;
                    if (i % 2 == 0)
                        queue.Enqueue(Entry(id, rating: 4m));
                    else
                        queue.Remove(id);
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        errors.Should().BeEmpty();
        var snapshot = queue.Snapshot(1);
        snapshot.Select(washer => washer.WasherId).Should().OnlyHaveUniqueItems();
        for (var id = 1; id <= 40; id++)
            queue.Contains(id).Should().Be(snapshot.Any(washer => washer.WasherId == id));
    }

    [Test]
    public void Fifo_ReturnsTheHead()
    {
        var queue = CreateQueue();
        queue.Enqueue(Entry(1, rating: 1m));
        queue.Enqueue(Entry(2, rating: 5m));
        queue.Enqueue(Entry(3, rating: 3m));

        Dequeue(queue, "Fifo").Should().Be(1);
        Dequeue(queue, "Fifo").Should().Be(2);
    }

    [Test]
    public void TopRated_PrefersRatingThenLongestWait()
    {
        var queue = CreateQueue();
        queue.Enqueue(Entry(3, rating: 3m, at: At(0)));
        queue.Enqueue(Entry(1, rating: 5m, at: At(10)));
        queue.Enqueue(Entry(2, rating: 5m, at: At(1)));

        Dequeue(queue, "TopRated").Should().Be(2);
        Dequeue(queue, "TopRated").Should().Be(1);
        Dequeue(queue, "TopRated").Should().Be(3);
    }

    [Test]
    public void Nearest_UsesHaversineAndPutsMissingLocationLast()
    {
        var queue = CreateQueue();
        queue.Enqueue(Entry(1, rating: 5m, latitude: 41m, longitude: -74m));
        queue.Enqueue(Entry(2, rating: 1m));
        queue.Enqueue(Entry(3, rating: 1m, latitude: 40.01m, longitude: -74m));

        var criteria = new WasherSelectionCriteria { Strategy = "Nearest", Latitude = 40m, Longitude = -74m };
        Next(queue, criteria).Should().Be(3);
        Next(queue, criteria).Should().Be(1);
        Next(queue, criteria).Should().Be(2);
    }

    [Test]
    public void ExcludeIds_AreLeftInTheQueue()
    {
        var queue = CreateQueue();
        queue.Enqueue(Entry(1, rating: 4m));
        queue.Enqueue(Entry(2, rating: 4m));
        queue.Enqueue(Entry(3, rating: 4m));

        queue.TryDequeueNext(1, [1], new WasherSelectionCriteria { Strategy = "Fifo" }, out var entry).Should().BeTrue();
        entry.WasherId.Should().Be(2);
        queue.Contains(1).Should().BeTrue();
        queue.Contains(2).Should().BeFalse();
    }

    [Test]
    public void DefaultStrategy_IsUsedWhenTheRequestOmitsOne()
    {
        var queue = CreateQueue(defaultStrategy: "TopRated");
        queue.Enqueue(Entry(1, rating: 2m, at: At(0)));
        queue.Enqueue(Entry(2, rating: 5m, at: At(5)));

        Dequeue(queue, strategy: null).Should().Be(2);
    }

    [Test]
    public void LeaseExpiry_ReturnsTheWasherToTheTail()
    {
        var clock = new FakeClock();
        var queue = CreateQueue(clock, leaseSeconds: 120);
        queue.Enqueue(Entry(1, rating: 4m));
        queue.Enqueue(Entry(2, rating: 4m));
        Dequeue(queue, "Fifo", clock).Should().Be(1);
        queue.Contains(1).Should().BeFalse();

        clock.UtcNow = clock.UtcNow.AddSeconds(119);
        queue.ExpiredOffers(clock.UtcNow).Should().BeEmpty();

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        var expired = queue.ExpiredOffers(clock.UtcNow);
        expired.Select(washer => washer.WasherId).Should().Equal(1);
        queue.ReturnToTail(1);

        queue.Snapshot(1).Select(washer => washer.WasherId).Should().Equal(2, 1);
        queue.Contains(1).Should().BeTrue();
    }

    [Test]
    public void Enqueue_IsIdempotent()
    {
        var queue = CreateQueue();
        queue.Enqueue(Entry(1, rating: 4m, at: At(0)));
        queue.Enqueue(Entry(1, rating: 5m, at: At(30)));

        var snapshot = queue.Snapshot(1);
        snapshot.Should().ContainSingle();
        snapshot[0].AvgRating.Should().Be(5m);
        snapshot[0].EnqueuedAtUtc.Should().Be(At(0));
    }

    private static int Dequeue(IWasherQueue queue, string? strategy, FakeClock? clock = null)
    {
        var ok = queue.TryDequeueNext(1, [], new WasherSelectionCriteria { Strategy = strategy }, out var entry);
        ok.Should().BeTrue();
        return entry.WasherId;
    }

    private static int Next(IWasherQueue queue, WasherSelectionCriteria criteria)
    {
        queue.TryDequeueNext(1, [], criteria, out var entry).Should().BeTrue();
        return entry.WasherId;
    }

    private static InMemoryWasherQueue CreateQueue(FakeClock? clock = null, string defaultStrategy = "Fifo", int leaseSeconds = 120)
    {
        return new InMemoryWasherQueue(
            [new FifoStrategy(), new TopRatedStrategy(), new NearestStrategy()],
            clock ?? new FakeClock(),
            Options.Create(new QueueOptions { DefaultStrategy = defaultStrategy, OfferLeaseSeconds = leaseSeconds }));
    }

    private static WasherEntry Entry(int id, decimal rating, decimal? latitude = null, decimal? longitude = null, DateTime? at = null) =>
        new()
        {
            WasherId = id,
            CityId = 1,
            AvgRating = rating,
            Latitude = latitude,
            Longitude = longitude,
            EnqueuedAtUtc = at ?? At(id)
        };

    private static DateTime At(int minutes) => new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
}

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
}
