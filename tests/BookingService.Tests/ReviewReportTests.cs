using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BookingService.Validators;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.BookingService.Tests;

[NonParallelizable]
public sealed class ReviewReportTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TC_20_CustomerReview_UpdatesWasherRating()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var washers = Washers();
        var service = Reviews(db, Customers(), washers, new BookingOutbox(Customers().Object, washers.Object));

        var review = await service.CreateAsync(
            new Caller(3, "Customer"),
            new CreateReviewDto { BookingId = booking.BookingId, Rating = 5, Comments = "Careful work" },
            CancellationToken.None);

        review.ReviewerRole.Should().Be("Customer");
        review.Rating.Should().Be(5);
        washers.Verify(directory => directory.RateAsync(7, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TC_21_WasherReview_DoesNotChangeWasherRating()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var washers = Washers();
        var service = Reviews(db, Customers(), washers, new FixedIdentityOutbox());

        var review = await service.CreateAsync(
            new Caller(4, "Washer"),
            new CreateReviewDto { BookingId = booking.BookingId, Rating = 4 },
            CancellationToken.None);

        review.ReviewerRole.Should().Be("Washer");
        washers.Verify(directory => directory.RateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        (await db.Reviews.CountAsync(row => row.ReviewerRole == ReviewerRole.Customer)).Should().Be(0);
    }

    [Test]
    public async Task TC_22_Leaderboard_DenseRankSharesTies()
    {
        await using var db = NewDb();
        db.Bookings.AddRange(
            Completed(1, 15),
            Completed(2, 15),
            Completed(3, 5),
            Cancelled(4, 100));
        await db.SaveChangesAsync();

        var customers = new Mock<ICustomerDirectory>();
        customers.Setup(directory => directory.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerSnapshot { CustomerId = 1, UserId = 11 });
        customers.Setup(directory => directory.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerSnapshot { CustomerId = 2, UserId = 12 });
        customers.Setup(directory => directory.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerSnapshot { CustomerId = 3, UserId = 13 });
        var users = new Mock<IUserDirectory>();
        users.Setup(directory => directory.FullNameAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync("Ada Lovelace");
        users.Setup(directory => directory.FullNameAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync("Grace Hopper");
        users.Setup(directory => directory.FullNameAsync(13, It.IsAny<CancellationToken>())).ReturnsAsync("Lin");
        var names = new DisplayNameLookup(
            customers.Object,
            new Mock<IWasherDirectory>().Object,
            users.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DisplayNameLookup>.Instance);
        var service = new LeaderboardService(new BookingInsightsRepository(db), names);

        var first = await service.TopAsync(10, CancellationToken.None);
        var second = await service.TopAsync(10, CancellationToken.None);

        first.Select(row => (row.CustomerId, row.Rank, row.DisplayName, row.WaterSavedLiters)).Should().Equal(
            (1, 1, "Ada L.", 15),
            (2, 1, "Grace H.", 15),
            (3, 2, "Lin", 5));
        second.Should().BeEquivalentTo(first);
        customers.Verify(directory => directory.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        (await service.TopAsync(1, CancellationToken.None)).Should().ContainSingle().Which.Rank.Should().Be(1);
        (await service.TopAsync(500, CancellationToken.None)).Should().HaveCount(3);
    }

    [Test]
    public async Task DuplicateReview_IsConflict()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var service = Reviews(db, Customers(), Washers(), new FixedIdentityOutbox());
        var dto = new CreateReviewDto { BookingId = booking.BookingId, Rating = 5 };
        await service.CreateAsync(new Caller(3, "Customer"), dto, CancellationToken.None);

        var act = () => service.CreateAsync(new Caller(3, "Customer"), dto, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Test]
    public async Task Washer_CannotReviewABookingHeWasNotOn()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var washers = Washers();
        washers.Setup(directory => directory.GetByUserAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WasherSnapshot { WasherId = 8, UserId = 9, CityId = 1, IsActive = true, IsQualified = true });
        var service = Reviews(db, Customers(), washers, new FixedIdentityOutbox());

        var act = () => service.CreateAsync(
            new Caller(9, "Washer"),
            new CreateReviewDto { BookingId = booking.BookingId, Rating = 2 },
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Test]
    public async Task RatingSyncFailure_KeepsTheReviewAndWritesRatingSyncPending()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var customers = Customers();
        var washers = Washers();
        washers.Setup(directory => directory.RateAsync(7, 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("washer down"));
        var service = Reviews(db, customers, washers, new BookingOutbox(customers.Object, washers.Object));

        var review = await service.CreateAsync(
            new Caller(3, "Customer"),
            new CreateReviewDto { BookingId = booking.BookingId, Rating = 5 },
            CancellationToken.None);

        review.ReviewId.Should().BeGreaterThan(0);
        var pending = await db.OutboxEvents.SingleAsync(evt => evt.EventType == "RatingSyncPending");
        Required(pending.Payload, "RatingSyncPending", booking.BookingId, 3, 4);
        washers.Verify(directory => directory.RateAsync(7, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AdminOrders_ApplyOnlyTheFiltersThatWereProvided()
    {
        await using var db = NewDb();
        db.Bookings.AddRange(
            Order("GCW-1", washerId: 1, BookingStatus.Completed, serviceId: 1, Now),
            Order("GCW-2", washerId: 1, BookingStatus.Pending, serviceId: 2, Now.AddHours(1)),
            Order("GCW-3", washerId: 2, BookingStatus.Completed, serviceId: 1, Now.AddHours(2)));
        await db.SaveChangesAsync();
        var service = new AdminOrderService(new BookingInsightsRepository(db));

        var byWasher = await service.ListAsync(new AdminOrderQuery { WasherId = 1 }, CancellationToken.None);
        var byWasherAndStatus = await service.ListAsync(
            new AdminOrderQuery { WasherId = 1, Status = "Completed" },
            CancellationToken.None);
        var pending = await service.ListAsync(new AdminOrderQuery { Status = "Pending" }, CancellationToken.None);

        byWasher.Items.Select(row => row.BookingNumber).Should().Equal("GCW-2", "GCW-1");
        byWasherAndStatus.Items.Should().ContainSingle().Which.BookingNumber.Should().Be("GCW-1");
        pending.Items.Should().ContainSingle().Which.Status.Should().Be("Pending");
    }

    [Test]
    public async Task Outbox_Failure_IncrementsRetryAndCapsBackoff()
    {
        var root = new InMemoryDatabaseRoot();
        var name = "outbox-backoff";
        var clock = new MutableClock(Now);
        await using (var seed = NewDb(name, root))
        {
            seed.OutboxEvents.Add(Event("BookingCreated", 0));
            seed.OutboxEvents.Add(Event("WashRequest", 4));
            await seed.SaveChangesAsync();
        }

        var publisher = new Mock<IOutboxEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payload rejected"));
        await using var host = Host(name, root, clock, publisher.Object);
        (await host.Publisher.PublishBatchAsync(CancellationToken.None)).Should().Be(2);

        await using var check = NewDb(name, root);
        var rows = await check.OutboxEvents.AsNoTracking().OrderBy(evt => evt.EventId).ToListAsync();
        rows[0].RetryCount.Should().Be(1);
        rows[0].NextAttemptAt.Should().Be(Now.AddMinutes(2));
        rows[0].ProcessedAt.Should().BeNull();
        rows[1].RetryCount.Should().Be(5);
        rows[1].NextAttemptAt.Should().Be(Now.AddMinutes(30));
    }

    [Test]
    public async Task Outbox_TwoPublishers_DoNotProcessTheSameEvent()
    {
        var root = new InMemoryDatabaseRoot();
        var name = "outbox-once";
        var seen = new ConcurrentBag<long>();
        var publisher = new Mock<IOutboxEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns<OutboxEvent, CancellationToken>(async (evt, _) =>
            {
                await Task.Delay(30);
                seen.Add(evt.EventId);
            });

        await using (var seed = NewDb(name, root))
        {
            seed.OutboxEvents.Add(Event("ServiceCompleted", 0));
            await seed.SaveChangesAsync();
        }

        await using var left = Host(name, root, new MutableClock(Now), publisher.Object);
        await using var right = Host(name, root, new MutableClock(Now), publisher.Object);
        await Task.WhenAll(
            left.Publisher.PublishBatchAsync(CancellationToken.None),
            right.Publisher.PublishBatchAsync(CancellationToken.None));

        seen.Should().ContainSingle();
        await using var check = NewDb(name, root);
        (await check.OutboxEvents.AsNoTracking().SingleAsync()).ProcessedAt.Should().Be(Now);
    }

    [Test]
    public async Task Outbox_Http400_IsDiscardedAfterThreeAttempts()
    {
        var root = new InMemoryDatabaseRoot();
        var name = "outbox-poison";
        var clock = new MutableClock(Now);
        await using (var seed = NewDb(name, root))
        {
            seed.OutboxEvents.Add(Event("BookingCreated", 0));
            await seed.SaveChangesAsync();
        }

        var publisher = new Mock<IOutboxEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Notification service returned 400.", null, HttpStatusCode.BadRequest));
        await using var host = Host(name, root, clock, publisher.Object);

        await host.Publisher.PublishBatchAsync(CancellationToken.None);
        clock.UtcNow = Now.AddMinutes(2);
        await host.Publisher.PublishBatchAsync(CancellationToken.None);

        await using (var mid = NewDb(name, root))
        {
            var pending = await mid.OutboxEvents.AsNoTracking().SingleAsync();
            pending.RetryCount.Should().Be(2);
            pending.ProcessedAt.Should().BeNull();
        }

        clock.UtcNow = Now.AddMinutes(6);
        await host.Publisher.PublishBatchAsync(CancellationToken.None);

        await using var check = NewDb(name, root);
        var row = await check.OutboxEvents.AsNoTracking().SingleAsync();
        row.RetryCount.Should().Be(3);
        row.ProcessedAt.Should().Be(Now.AddMinutes(6));
        row.LastError.Should().Contain("400");
    }

    [Test]
    public async Task Outbox_SkipsEventsOwnedByDedicatedWorkers()
    {
        var root = new InMemoryDatabaseRoot();
        var name = "outbox-owned";
        var seen = new List<string>();
        var publisher = new Mock<IOutboxEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEvent, CancellationToken>((evt, _) => seen.Add(evt.EventType))
            .Returns(Task.CompletedTask);
        await using (var seed = NewDb(name, root))
        {
            seed.OutboxEvents.Add(Event("BookingCreated", 0));
            seed.OutboxEvents.Add(Event("RatingSyncPending", 0));
            seed.OutboxEvents.Add(Event("RefundPending", 0));
            await seed.SaveChangesAsync();
        }

        await using var host = Host(name, root, new MutableClock(Now), publisher.Object, ["RatingSyncPending", "RefundPending"]);
        (await host.Publisher.PublishBatchAsync(CancellationToken.None)).Should().Be(1);
        seen.Should().Equal("BookingCreated");

        await using var check = NewDb(name, root);
        var rows = await check.OutboxEvents.AsNoTracking().OrderBy(evt => evt.EventId).ToListAsync();
        rows.Single(evt => evt.EventType == "BookingCreated").ProcessedAt.Should().Be(Now);
        rows.Single(evt => evt.EventType == "RatingSyncPending").ProcessedAt.Should().BeNull();
        rows.Single(evt => evt.EventType == "RefundPending").ProcessedAt.Should().BeNull();
    }

    [Test]
    public async Task RatingSync_FailsThenSucceeds_UpdatesAverageOnce()
    {
        await using var db = NewDb();
        var booking = await SeedCompletedAsync(db, washerId: 7);
        var customers = Customers();
        var washers = Washers();
        var attempts = 0;
        var updates = 0;
        decimal average = 0m;
        washers.Setup(directory => directory.RateAsync(7, 5, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new HttpRequestException("washer down");
                updates++;
                average = decimal.Round((average * (updates - 1) + 5) / updates, 2, MidpointRounding.AwayFromZero);
                return Task.CompletedTask;
            });
        var service = Reviews(db, customers, washers, new BookingOutbox(customers.Object, washers.Object));
        var review = await service.CreateAsync(
            new Caller(3, "Customer"),
            new CreateReviewDto { BookingId = booking.BookingId, Rating = 5 },
            CancellationToken.None);

        var pending = await db.OutboxEvents.SingleAsync(evt => evt.EventType == "RatingSyncPending");
        var originalPayload = pending.Payload;
        originalPayload.Should().Contain($"\"reviewId\":{review.ReviewId}");
        originalPayload.Should().NotContain("appliedReviewId");
        var clock = new MutableClock(Now);
        var processor = new RatingSyncProcessor(db, washers.Object, clock, NullLogger<RatingSyncProcessor>.Instance);

        await processor.ProcessBatchAsync(CancellationToken.None);
        (await db.OutboxEvents.SingleAsync(evt => evt.EventId == pending.EventId)).ProcessedAt.Should().BeNull();
        updates.Should().Be(0);

        clock.UtcNow = Now.AddMinutes(2);
        await processor.ProcessBatchAsync(CancellationToken.None);

        var synced = await db.OutboxEvents.SingleAsync(evt => evt.EventId == pending.EventId);
        synced.ProcessedAt.Should().Be(clock.UtcNow);
        synced.Payload.Should().Contain($"\"appliedReviewId\":{review.ReviewId}");
        updates.Should().Be(1);
        average.Should().Be(5.00m);

        db.OutboxEvents.Add(new OutboxEvent
        {
            EventType = "RatingSyncPending",
            Payload = originalPayload,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        await processor.ProcessBatchAsync(CancellationToken.None);

        updates.Should().Be(1);
        attempts.Should().Be(3);
    }

    [Test]
    public void OutboxPayload_EveryEventType_HasTheRequiredShape()
    {
        var when = Now;
        foreach (var eventType in BookingEventPayload.EventTypes)
        {
            var washerUserId = eventType == "BookingCreated" ? (int?)null : 4;
            var evt = BookingEventPayload.Create(15, "GCW-20261121-000015", "Pending", eventType, 3, washerUserId, when, "note");
            evt.EventType.Should().Be(eventType);
            Required(evt.Payload, eventType, 15, 3, washerUserId);
        }
    }

    private static void Required(string payload, string eventType, int bookingId, int customerUserId, int? washerUserId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("eventType").GetString().Should().Be(eventType);
        root.GetProperty("bookingId").GetInt32().Should().Be(bookingId);
        root.GetProperty("bookingNumber").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("customerUserId").GetInt32().Should().Be(customerUserId);
        if (washerUserId is int washer)
            root.GetProperty("washerUserId").GetInt32().Should().Be(washer);
        else
            root.GetProperty("washerUserId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("referenceId").GetString().Should().Be(bookingId.ToString());
        root.GetProperty("occurredAt").GetDateTime().Should().Be(Now);
    }

    private static ReviewService Reviews(BookingDbContext db, Mock<ICustomerDirectory> customers, Mock<IWasherDirectory> washers, IBookingOutbox outbox) =>
        new(
            new ReviewRepository(db),
            customers.Object,
            washers.Object,
            outbox,
            new CreateReviewDtoValidator(),
            new MutableClock(Now),
            NullLogger<ReviewService>.Instance);

    private static Mock<ICustomerDirectory> Customers()
    {
        var customers = new Mock<ICustomerDirectory>();
        var profile = new CustomerSnapshot { CustomerId = 1, UserId = 3, CityId = 1, Address = "10 Main St" };
        customers.Setup(directory => directory.GetByUserAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        customers.Setup(directory => directory.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        return customers;
    }

    private static Mock<IWasherDirectory> Washers()
    {
        var washers = new Mock<IWasherDirectory>();
        var profile = new WasherSnapshot { WasherId = 7, UserId = 4, CityId = 1, IsActive = true, IsQualified = true };
        washers.Setup(directory => directory.GetByUserAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        washers.Setup(directory => directory.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        washers.Setup(directory => directory.RateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return washers;
    }

    private static async Task<Booking> SeedCompletedAsync(BookingDbContext db, int washerId)
    {
        var booking = Completed(1, 15);
        booking.WasherId = washerId;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static Booking Completed(int customerId, int liters)
    {
        var booking = Order($"GCW-{customerId}-{liters}-{Guid.NewGuid():N}"[..20], 7, BookingStatus.Completed, 1, Now);
        booking.CustomerId = customerId;
        booking.WaterSavedLiters = liters;
        booking.CompletedAt = Now;
        return booking;
    }

    private static Booking Cancelled(int customerId, int liters)
    {
        var booking = Completed(customerId, liters);
        booking.Status = BookingStatus.Cancelled;
        booking.CompletedAt = null;
        return booking;
    }

    private static Booking Order(string number, int? washerId, BookingStatus status, int serviceId, DateTime when) => new()
    {
        BookingNumber = number,
        CustomerId = 1,
        VehicleId = 1,
        ServiceId = serviceId,
        WasherId = washerId,
        CityId = 1,
        BookingType = BookingType.Immediate,
        BookingDate = when,
        EstimatedDurationMin = 30,
        Address = "10 Main St",
        ServiceNameSnapshot = "Basic",
        ServicePriceSnapshot = 19.99m,
        TotalAmount = 19.99m,
        Status = status,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static OutboxEvent Event(string type, int retryCount) => new()
    {
        EventType = type,
        Payload = "{}",
        CreatedAt = Now,
        NextAttemptAt = Now,
        RetryCount = retryCount
    };

    private static PublisherHost Host(
        string name,
        InMemoryDatabaseRoot root,
        IClock clock,
        IOutboxEventPublisher publisher,
        IEnumerable<string>? excluded = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<BookingDbContext>(builder => builder
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddSingleton(publisher);
        var provider = services.BuildServiceProvider();
        var options = new OutboxPublisherOptions<BookingDbContext>
        {
            ExcludedEventTypes = new HashSet<string>(excluded ?? [], StringComparer.Ordinal)
        };
        var hosted = new OutboxPublisherBackgroundService<BookingDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<OutboxPublisherBackgroundService<BookingDbContext>>.Instance,
            options);
        return new PublisherHost(provider, hosted);
    }

    private static BookingDbContext NewDb() => NewDb(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());

    private static BookingDbContext NewDb(string name, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options);
    }

    private sealed class PublisherHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public PublisherHost(ServiceProvider provider, OutboxPublisherBackgroundService<BookingDbContext> publisher)
        {
            _provider = provider;
            Publisher = publisher;
        }

        public OutboxPublisherBackgroundService<BookingDbContext> Publisher { get; }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
