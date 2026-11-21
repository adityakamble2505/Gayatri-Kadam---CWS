using FluentAssertions;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.BookingService.Tests;

[NonParallelizable]
public class DispatchTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static int _numbers = 100;

    [Test]
    public async Task FirstWasher_AcceptsTheOffer()
    {
        await using var lab = await Lab.Start(options: Quick(offerSeconds: 30), next: Queue(7));
        var booking = await lab.AddImmediateAsync();
        lab.Coordinator.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));

        await lab.WaitForOfferAsync(booking.BookingId, 7);
        var accepted = await lab.Workflow.AcceptAsync(new Caller(4, "Washer"), booking.BookingId, CancellationToken.None);

        accepted.Status.Should().Be(nameof(BookingStatus.Accepted));
        accepted.WasherId.Should().Be(7);
        (await lab.OffersAsync(booking.BookingId)).Single(offer => offer.WasherId == 7).Response.Should().Be(OfferResponse.Accepted);
        (await lab.EventsAsync()).Should().Contain("WashRequest").And.Contain("WasherAccepted");
        lab.Washers.Verify(washers => washers.ConfirmAsync(7, booking.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Decline_ThenSecondWasher_Accepts()
    {
        await using var lab = await Lab.Start(options: Quick(offerSeconds: 30), next: Queue(7, 8));
        var booking = await lab.AddImmediateAsync();
        lab.Coordinator.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));

        await lab.WaitForOfferAsync(booking.BookingId, 7);
        await lab.Workflow.DeclineAsync(new Caller(4, "Washer"), booking.BookingId, CancellationToken.None);
        await lab.WaitForOfferAsync(booking.BookingId, 8);
        var accepted = await lab.Workflow.AcceptAsync(new Caller(5, "Washer"), booking.BookingId, CancellationToken.None);

        accepted.WasherId.Should().Be(8);
        var offers = await lab.OffersAsync(booking.BookingId);
        offers.Single(offer => offer.WasherId == 7).Response.Should().Be(OfferResponse.Declined);
        offers.Single(offer => offer.WasherId == 8).Response.Should().Be(OfferResponse.Accepted);
        lab.Washers.Verify(washers => washers.ReleaseOfferAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Timeout_OffersTheNextWasher()
    {
        await using var lab = await Lab.Start(options: Quick(offerSeconds: 0), next: (call, token) =>
        {
            if (call == 0)
                return Task.FromResult(new WasherOffer(7, 4));
            if (call == 1)
                return Task.FromResult(new WasherOffer(8, 5));
            return Hanging(token);
        });
        var booking = await lab.AddImmediateAsync();
        lab.Coordinator.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));

        await lab.WaitUntilAsync(async () =>
        {
            var offers = await lab.OffersAsync(booking.BookingId);
            return offers.Count >= 2 && offers.All(offer => offer.Response == OfferResponse.Expired);
        });

        var offers = await lab.OffersAsync(booking.BookingId);
        offers.Should().HaveCountGreaterOrEqualTo(2);
        offers.Select(offer => offer.WasherId).Should().Contain(new[] { 7, 8 });
    }

    [Test]
    public async Task AllAttemptsExhausted_FlagsAdminAttention()
    {
        await using var lab = await Lab.Start(
            options: Quick(offerSeconds: 0, maxAttempts: 3),
            next: (_, _) => throw new NotFoundException("No washer is available."));
        var booking = await lab.AddImmediateAsync();
        lab.Coordinator.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));

        await lab.WaitUntilAsync(async () =>
        {
            var stored = await lab.FindAsync(booking.BookingId);
            return stored.NeedsAdminAttention && stored.Status == BookingStatus.Pending;
        });

        (await lab.EventsAsync()).Should().Contain("AdminAlert");
        (await lab.FindAsync(booking.BookingId)).WasherId.Should().BeNull();
    }

    [Test]
    public async Task CustomerCancel_MidDispatch_ExpiresTheOfferAndReleasesTheWasher()
    {
        await using var lab = await Lab.Start(options: Quick(offerSeconds: 30), next: Queue(7));
        var booking = await lab.AddImmediateAsync();
        lab.Coordinator.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));
        await lab.WaitForOfferAsync(booking.BookingId, 7);

        var cancelled = await lab.Workflow.CancelAsync(
            new Caller(3, "Customer"),
            booking.BookingId,
            new DTOs.CancelBookingDto { Reason = "Changed plans" },
            CancellationToken.None);

        cancelled.Status.Should().Be(nameof(BookingStatus.Cancelled));
        (await lab.OffersAsync(booking.BookingId)).Single().Response.Should().Be(OfferResponse.Expired);
        lab.Washers.Verify(washers => washers.ReleaseOfferAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Q2_TwoWorkers_ClaimEachBookingOnce()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "q2-claim";
        await using var seed = NewDb(name, root);
        var booking = Scheduled(Now.AddHours(1));
        seed.Bookings.Add(booking);
        await seed.SaveChangesAsync();

        await using var leftDb = NewDb(name, root);
        await using var rightDb = NewDb(name, root);
        var left = new BookingRepository(leftDb, new FixedIdentityOutbox());
        var right = new BookingRepository(rightDb, new FixedIdentityOutbox());
        var claimed = await Task.WhenAll(
            left.ClaimScheduledAsync(Now, CancellationToken.None),
            right.ClaimScheduledAsync(Now, CancellationToken.None));

        claimed.SelectMany(batch => batch.Select(row => row.BookingId)).Should().Equal(booking.BookingId);
        await using var check = NewDb(name, root);
        (await check.Bookings.AsNoTracking().SingleAsync()).DispatchStartedAt.Should().Be(Now);
    }

    [Test]
    public async Task Escalation_AfterThirtyMinutes_AutoAssigns()
    {
        await using var lab = await Lab.Start(options: Quick(windowMinutes: 30), next: Queue(7));
        var booking = Scheduled(Now.AddHours(1));
        booking.DispatchStartedAt = Now.AddMinutes(-30);
        lab.Db.Bookings.Add(booking);
        await lab.Db.SaveChangesAsync();

        await lab.Maintenance.EscalateDueAsync(CancellationToken.None);

        var stored = await lab.FindAsync(booking.BookingId);
        stored.Status.Should().Be(BookingStatus.Accepted);
        stored.WasherId.Should().Be(7);
        stored.NeedsAdminAttention.Should().BeTrue();
        (await lab.EventsAsync()).Should().Contain("AdminAlert");
        lab.Washers.Verify(washers => washers.ConfirmAsync(7, booking.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Reminder_IsSentOnce()
    {
        await using var lab = await Lab.Start(options: Quick(), next: Queue(7));
        var booking = Scheduled(Now.AddMinutes(120));
        booking.Status = BookingStatus.Accepted;
        booking.WasherId = 7;
        lab.Db.Bookings.Add(booking);
        await lab.Db.SaveChangesAsync();

        await lab.Maintenance.RemindAsync(CancellationToken.None);
        await lab.Maintenance.RemindAsync(CancellationToken.None);

        (await lab.EventsAsync()).Count(type => type == "Reminder2h").Should().Be(1);
    }

    [Test]
    public async Task Restart_RearmsLiveOffers_AndExpiresOverdueOnes()
    {
        await using var lab = await Lab.Start(options: Quick(offerSeconds: 30), next: (_, _) => throw new NotFoundException("No washer is available."));
        var live = await lab.AddImmediateAsync();
        var stale = await lab.AddImmediateAsync();
        lab.Db.BookingOffers.Add(new BookingOffer
        {
            BookingId = live.BookingId,
            WasherId = 7,
            OfferedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.AddMinutes(20),
            Response = OfferResponse.Pending
        });
        lab.Db.BookingOffers.Add(new BookingOffer
        {
            BookingId = stale.BookingId,
            WasherId = 8,
            OfferedAt = Now.AddMinutes(-5),
            ExpiresAt = Now.AddMinutes(-1),
            Response = OfferResponse.Pending
        });
        await lab.Db.SaveChangesAsync();

        await lab.Recovery.RecoverAsync(CancellationToken.None);
        await lab.WaitUntilAsync(() => Task.FromResult(lab.Coordinator.HoldsOffer(live.BookingId, 7)));

        (await lab.OffersAsync(live.BookingId)).Single().Response.Should().Be(OfferResponse.Pending);
        (await lab.OffersAsync(stale.BookingId)).Single().Response.Should().Be(OfferResponse.Expired);
        lab.Washers.Verify(washers => washers.ReleaseOfferAsync(8, It.IsAny<CancellationToken>()), Times.Once);
        lab.Washers.Verify(washers => washers.ReleaseOfferAsync(7, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DispatchOptions Quick(int offerSeconds = 60, int maxAttempts = 5, int windowMinutes = 30) => new()
    {
        ImmediateStrategy = "Nearest",
        ScheduledStrategy = "TopRated",
        OfferSeconds = offerSeconds,
        RetryDelaySeconds = 0,
        MaxAttempts = maxAttempts,
        ScheduledWindowMinutes = windowMinutes
    };

    private static Func<int, CancellationToken, Task<WasherOffer>> Queue(params int[] washerIds) => (call, _) =>
    {
        var washerId = washerIds[Math.Min(call, washerIds.Length - 1)];
        return Task.FromResult(new WasherOffer(washerId, washerId == 8 ? 5 : 4));
    };

    private static async Task<WasherOffer> Hanging(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new WasherOffer(0, 0);
    }

    private static Booking Scheduled(DateTime when)
    {
        var booking = BookingRow(BookingType.Scheduled, BookingStatus.Scheduled, when);
        return booking;
    }

    private static Booking BookingRow(BookingType type, BookingStatus status, DateTime when) => new()
    {
        BookingNumber = $"GCW-20261121-{Interlocked.Increment(ref _numbers):D6}",
        CustomerId = 1,
        VehicleId = 1,
        ServiceId = 1,
        CityId = 1,
        BookingType = type,
        BookingDate = when,
        EstimatedDurationMin = 30,
        Address = "10 Main St",
        Latitude = 40.712800m,
        Longitude = -74.006000m,
        ServiceNameSnapshot = "Basic",
        ServicePriceSnapshot = 19.99m,
        TotalAmount = 19.99m,
        Status = status,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static BookingDbContext NewDb(string name, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options);
    }

    private sealed class Lab : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly CancellationTokenSource _stop = new();
        private int _calls;

        private Lab(ServiceProvider provider, AsyncServiceScope scope, DispatchCoordinator coordinator, DispatchMaintenance maintenance, DispatchRecoveryService recovery, Mock<IWasherDirectory> washers)
        {
            _provider = provider;
            _scope = scope;
            Coordinator = coordinator;
            Maintenance = maintenance;
            Recovery = recovery;
            Washers = washers;
        }

        public BookingDbContext Db { get; private set; } = null!;

        public BookingWorkflow Workflow { get; private set; } = null!;

        public DispatchCoordinator Coordinator { get; }

        public DispatchMaintenance Maintenance { get; }

        public DispatchRecoveryService Recovery { get; }

        public Mock<IWasherDirectory> Washers { get; }

        public static async Task<Lab> Start(DispatchOptions options, Func<int, CancellationToken, Task<WasherOffer>>? next = null)
        {
            var washers = new Mock<IWasherDirectory>();
            washers.Setup(directory => directory.GetByUserAsync(4, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WasherSnapshot { WasherId = 7, UserId = 4, CityId = 1, IsActive = true, IsQualified = true });
            washers.Setup(directory => directory.GetByUserAsync(5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WasherSnapshot { WasherId = 8, UserId = 5, CityId = 1, IsActive = true, IsQualified = true });
            washers.Setup(directory => directory.ConfirmAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            washers.Setup(directory => directory.ReleaseOfferAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            washers.Setup(directory => directory.StageAssignedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            washers.Setup(directory => directory.MakeAvailableAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var customers = new Mock<ICustomerDirectory>();
            customers.Setup(directory => directory.GetByUserAsync(3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CustomerSnapshot { CustomerId = 1, UserId = 3, CityId = 1, Address = "10 Main St" });
            var catalog = new Mock<ICatalogPricing>();
            var cities = new Mock<IActiveCityCatalog>();
            var images = new Mock<IBookingImageStore>();

            var root = new InMemoryDatabaseRoot();
            var name = Guid.NewGuid().ToString("N");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BookingDbContext>(builder => builder
                .UseInMemoryDatabase(name, root)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
            services.AddSingleton<IBookingOutbox, FixedIdentityOutbox>();
            services.AddScoped<IBookingRepository, BookingRepository>();
            services.AddSingleton<IClock>(new MutableClock(Now));
            services.AddSingleton(washers.Object);
            services.AddSingleton<IOptions<DispatchOptions>>(Microsoft.Extensions.Options.Options.Create(options));
            services.AddSingleton<DispatchCoordinator>();
            services.AddSingleton<IDispatchQueue>(provider => provider.GetRequiredService<DispatchCoordinator>());
            services.AddSingleton<DispatchMaintenance>();
            services.AddSingleton<DispatchRecoveryService>();

            var provider = services.BuildServiceProvider();
            var lab = new Lab(
                provider,
                provider.CreateAsyncScope(),
                provider.GetRequiredService<DispatchCoordinator>(),
                provider.GetRequiredService<DispatchMaintenance>(),
                provider.GetRequiredService<DispatchRecoveryService>(),
                washers);
            var scopeProvider = lab._scope.ServiceProvider;
            lab.Db = scopeProvider.GetRequiredService<BookingDbContext>();
            lab.Workflow = new BookingWorkflow(
                scopeProvider.GetRequiredService<IBookingRepository>(),
                customers.Object,
                washers.Object,
                catalog.Object,
                cities.Object,
                images.Object,
                new Validators.CreateBookingDtoValidator(),
                new Validators.ScheduleBookingDtoValidator(),
                new Validators.CancelBookingDtoValidator(),
                new Validators.OrderListQueryValidator(),
                provider.GetRequiredService<IDispatchQueue>(),
                NullPaymentCompensation.Instance,
                provider.GetRequiredService<IClock>(),
                NullLogger<BookingWorkflow>.Instance);

            washers.Setup(directory => directory.NextAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<int>>(),
                    It.IsAny<string?>(),
                    It.IsAny<decimal?>(),
                    It.IsAny<decimal?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((int cityId, IReadOnlyCollection<int> exclude, string? strategy, decimal? latitude, decimal? longitude, CancellationToken cancellationToken) =>
                {
                    var call = lab._calls++;
                    if (next is null)
                        throw new NotFoundException("No washer is available.");
                    return next(call, cancellationToken);
                });

            await lab.Coordinator.StartAsync(lab._stop.Token);
            return lab;
        }

        public async Task<Booking> AddImmediateAsync()
        {
            var booking = BookingRow(BookingType.Immediate, BookingStatus.Pending, Now);
            Db.Bookings.Add(booking);
            await Db.SaveChangesAsync();
            return booking;
        }

        public async Task<IReadOnlyList<BookingOffer>> OffersAsync(int bookingId)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<BookingDbContext>().BookingOffers.AsNoTracking()
                .Where(offer => offer.BookingId == bookingId)
                .OrderBy(offer => offer.OfferId)
                .ToListAsync();
        }

        public async Task<Booking> FindAsync(int bookingId)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<BookingDbContext>().Bookings.AsNoTracking()
                .SingleAsync(booking => booking.BookingId == bookingId);
        }

        public async Task<IReadOnlyList<string>> EventsAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<BookingDbContext>().Set<OutboxEvent>().AsNoTracking()
                .Select(evt => evt.EventType)
                .ToListAsync();
        }

        public async Task WaitForOfferAsync(int bookingId, int washerId) =>
            await WaitUntilAsync(async () => (await OffersAsync(bookingId)).Any(offer => offer.WasherId == washerId && offer.Response == OfferResponse.Pending));

        public async Task WaitUntilAsync(Func<Task<bool>> ready)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (await ready())
                    return;
                await Task.Delay(20);
            }

            throw new TimeoutException("Dispatch condition was not reached.");
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await Coordinator.StopAsync(CancellationToken.None);
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
