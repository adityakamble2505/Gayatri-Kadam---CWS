using System.Net;
using System.Text;
using FluentAssertions;
using FluentValidation;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BookingService.Validators;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.BookingService.Tests;

[NonParallelizable]
public class BookingFlowTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    [Test]
    public async Task TC_11_WashNow_PersistsQuoteSnapshotAndBookingCreated()
    {
        await using var harness = NewHarness();
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(promo: "welcome10"), CancellationToken.None);

        created.Status.Should().Be(nameof(BookingStatus.Pending));
        created.BookingType.Should().Be(nameof(BookingType.Immediate));
        created.BookingDate.Should().Be(Now);
        created.TotalAmount.Should().Be(31.48m);
        created.PromoDiscount.Should().Be(3.50m);
        created.ServiceNameSnapshot.Should().Be("Basic");
        created.WaterSavedLiters.Should().Be(15);
        created.EstimatedDurationMin.Should().Be(30);
        created.BookingNumber.Should().Be($"GCW-{Now:yyyyMMdd}-{created.BookingId:D6}");
        created.AddOns.Should().ContainSingle(addOn => addOn.AddOnId == 1 && addOn.Price == 14.99m);

        var stored = await harness.Db.Bookings.Include(booking => booking.AddOns).SingleAsync();
        stored.Status.Should().Be(BookingStatus.Pending);
        stored.BookingNumber.Should().Be(created.BookingNumber);
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("BookingCreated");
        (await harness.Db.BookingStatusHistory.SingleAsync()).ToStatus.Should().Be(nameof(BookingStatus.Pending));
        harness.Catalog.Verify(catalog => catalog.RedeemAsync("WELCOME10", 34.98m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TC_12_Schedule_StoresScheduledStatusAndDate()
    {
        await using var harness = NewHarness();
        var when = Now.AddHours(3);
        var created = await harness.Workflow.ScheduleAsync(Customer(), Schedule(when), CancellationToken.None);

        created.Status.Should().Be(nameof(BookingStatus.Scheduled));
        created.BookingType.Should().Be(nameof(BookingType.Scheduled));
        created.BookingDate.Should().Be(when);
    }

    [Test]
    public async Task TC_13_Accept_AssignsWasherAndConfirms()
    {
        await using var harness = NewHarness();
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        await SeedOffer(harness.Db, created.BookingId, 7);

        var accepted = await harness.Workflow.AcceptAsync(Washer(), created.BookingId, CancellationToken.None);

        accepted.Status.Should().Be(nameof(BookingStatus.Accepted));
        accepted.WasherId.Should().Be(7);
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("WasherAccepted");
        harness.Washers.Verify(washers => washers.ConfirmAsync(7, created.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TC_14_Start_MovesToInProgress()
    {
        await using var harness = NewHarness();
        var bookingId = await AcceptNewAsync(harness);

        var started = await harness.Workflow.StartAsync(Washer(), bookingId, CancellationToken.None);

        started.Status.Should().Be(nameof(BookingStatus.InProgress));
        started.StartedAt.Should().Be(Now);
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("ServiceStarted");
        harness.Washers.Verify(washers => washers.StartAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TC_15_Complete_RequiresPhotoAndRecordsServiceCompleted()
    {
        await using var harness = NewHarness();
        var bookingId = await AcceptNewAsync(harness);
        await harness.Workflow.StartAsync(Washer(), bookingId, CancellationToken.None);

        var act = () => harness.Workflow.CompleteAsync(Washer(), bookingId, "image/jpeg", [], CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();

        var completed = await harness.Workflow.CompleteAsync(Washer(), bookingId, "image/jpeg", Jpeg, CancellationToken.None);

        completed.Status.Should().Be(nameof(BookingStatus.Completed));
        completed.AfterWashPhotoUrl.Should().Be("/uploads/bookings/photo.jpg");
        completed.CompletedAt.Should().Be(Now);
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("ServiceCompleted");
        harness.Washers.Verify(washers => washers.CompleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TC_16_CustomerCancel_ReleasesWasherAndPromo()
    {
        await using var harness = NewHarness();
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(promo: "welcome10"), CancellationToken.None);
        await SeedOffer(harness.Db, created.BookingId, 7);
        await harness.Workflow.AcceptAsync(Washer(), created.BookingId, CancellationToken.None);

        var cancelled = await harness.Workflow.CancelAsync(
            Customer(),
            created.BookingId,
            new CancelBookingDto { Reason = "Changed plans" },
            CancellationToken.None);

        cancelled.Status.Should().Be(nameof(BookingStatus.Cancelled));
        cancelled.CancelledBy.Should().Be(nameof(CancelledBy.Customer));
        cancelled.CancelReason.Should().Be("Changed plans");
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("BookingCancelled");
        harness.Washers.Verify(washers => washers.ReleaseOfferAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        harness.Washers.Verify(washers => washers.MakeAvailableAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        harness.Catalog.Verify(catalog => catalog.ReleaseAsync("WELCOME10", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Cancel_WithSuccessfulPayment_CallsRefundOnce()
    {
        var calls = new List<HttpRequestMessage>();
        await using var harness = RefundHarness(calls, lookupStatus: "Success", refundStatus: HttpStatusCode.OK);

        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        var cancelled = await harness.Workflow.CancelAsync(Customer(), created.BookingId, new CancelBookingDto { Reason = "Changed plans" }, CancellationToken.None);

        cancelled.Status.Should().Be(nameof(BookingStatus.Cancelled));
        calls.Count(call => call.RequestUri!.AbsolutePath.EndsWith("refund-for-booking", StringComparison.Ordinal)).Should().Be(1);
    }

    [Test]
    public async Task Cancel_WhenRefundFails_WritesRefundPendingAndStillCancels()
    {
        var calls = new List<HttpRequestMessage>();
        await using var harness = RefundHarness(calls, lookupStatus: "Success", refundStatus: HttpStatusCode.BadGateway);

        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        var cancelled = await harness.Workflow.CancelAsync(Customer(), created.BookingId, new CancelBookingDto { Reason = "Changed plans" }, CancellationToken.None);

        cancelled.Status.Should().Be(nameof(BookingStatus.Cancelled));
        var pending = await harness.Db.OutboxEvents.SingleAsync(evt => evt.EventType == "RefundPending");
        pending.Payload.Should().Contain("\"eventType\":\"RefundPending\"");
        pending.Payload.Should().Contain($"\"bookingId\":{created.BookingId}");
        pending.Payload.Should().Contain($"\"bookingNumber\":\"{created.BookingNumber}\"");
        pending.Payload.Should().Contain("\"customerUserId\":3");
        pending.Payload.Should().Contain("\"washerUserId\":null");
        pending.Payload.Should().Contain("\"status\":\"Cancelled\"");
        pending.Payload.Should().Contain($"\"referenceId\":\"{created.BookingId}\"");
        pending.Payload.Should().Contain("\"occurredAt\":");
    }

    [Test]
    public async Task Cancel_WithoutPayment_DoesNotCallRefund()
    {
        var calls = new List<HttpRequestMessage>();
        await using var harness = RefundHarness(calls, lookupStatus: null, refundStatus: HttpStatusCode.OK);

        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        var cancelled = await harness.Workflow.CancelAsync(Customer(), created.BookingId, new CancelBookingDto { Reason = "Changed plans" }, CancellationToken.None);

        cancelled.Status.Should().Be(nameof(BookingStatus.Cancelled));
        calls.Should().NotContain(call => call.RequestUri!.AbsolutePath.EndsWith("refund-for-booking", StringComparison.Ordinal));
        (await harness.Db.OutboxEvents.AnyAsync(evt => evt.EventType == "RefundPending")).Should().BeFalse();
    }

    [Test]
    public async Task RefundRetry_FailsThenSucceeds_AndDoesNotRefundTwice()
    {
        var failedCalls = new List<HttpRequestMessage>();
        await using var harness = RefundHarness(failedCalls, lookupStatus: "Success", refundStatus: HttpStatusCode.BadGateway);
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        await harness.Workflow.CancelAsync(Customer(), created.BookingId, new CancelBookingDto { Reason = "Changed plans" }, CancellationToken.None);
        var pending = await harness.Db.OutboxEvents.SingleAsync(evt => evt.EventType == "RefundPending");
        pending.ProcessedAt.Should().BeNull();

        var retried = new List<HttpRequestMessage>();
        var http = new HttpClient(new RefundHandler(retried, "Success", HttpStatusCode.OK))
        {
            BaseAddress = new Uri("http://payments.test/")
        };
        var compensation = new PaymentCompensation(
            http,
            new BookingRepository(harness.Db, new FixedIdentityOutbox()),
            new FakeClock(Now),
            NullLogger<PaymentCompensation>.Instance);
        var processor = new RefundRetryProcessor(
            harness.Db,
            compensation,
            new FakeClock(Now),
            NullLogger<RefundRetryProcessor>.Instance);

        await processor.ProcessBatchAsync(CancellationToken.None);
        retried.Count(call => call.RequestUri!.AbsolutePath.EndsWith("refund-for-booking", StringComparison.Ordinal)).Should().Be(1);
        (await harness.Db.OutboxEvents.SingleAsync(evt => evt.EventId == pending.EventId)).ProcessedAt.Should().NotBeNull();

        await processor.ProcessBatchAsync(CancellationToken.None);
        retried.Count(call => call.RequestUri!.AbsolutePath.EndsWith("refund-for-booking", StringComparison.Ordinal)).Should().Be(1);
    }

    [Test]
    public async Task RefundRetry_WhenPaymentStaysDown_BacksOffWithoutDiscarding()
    {
        var calls = new List<HttpRequestMessage>();
        await using var harness = RefundHarness(calls, lookupStatus: "Success", refundStatus: HttpStatusCode.BadGateway);
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        await harness.Workflow.CancelAsync(Customer(), created.BookingId, new CancelBookingDto { Reason = "Changed plans" }, CancellationToken.None);
        calls.Clear();

        var processor = new RefundRetryProcessor(
            harness.Db,
            new PaymentCompensation(
                new HttpClient(new RefundHandler(calls, "Success", HttpStatusCode.BadGateway)) { BaseAddress = new Uri("http://payments.test/") },
                new BookingRepository(harness.Db, new FixedIdentityOutbox()),
                new FakeClock(Now),
                NullLogger<PaymentCompensation>.Instance),
            new FakeClock(Now),
            NullLogger<RefundRetryProcessor>.Instance);

        await processor.ProcessBatchAsync(CancellationToken.None);
        var pending = await harness.Db.OutboxEvents.SingleAsync(evt => evt.EventType == "RefundPending");
        pending.ProcessedAt.Should().BeNull();
        pending.RetryCount.Should().Be(1);
        pending.NextAttemptAt.Should().Be(Now.AddMinutes(2));
    }

    private static Harness RefundHarness(List<HttpRequestMessage> calls, string? lookupStatus, HttpStatusCode refundStatus)
    {
        var root = new InMemoryDatabaseRoot();
        var db = NewDb(Guid.NewGuid().ToString("N"), root);
        var repository = new BookingRepository(db, new FixedIdentityOutbox());
        var http = new HttpClient(new RefundHandler(calls, lookupStatus, refundStatus))
        {
            BaseAddress = new Uri("http://payments.test/")
        };
        var compensation = new PaymentCompensation(
            http,
            repository,
            new FakeClock(Now),
            NullLogger<PaymentCompensation>.Instance);
        return NewHarness(db, compensation, repository);
    }

    [Test]
    public void StateMachine_AllowsOnlyTheDocumentedTransitions()
    {
        var allowed = new HashSet<(BookingStatus From, BookingStatus To)>
        {
            (BookingStatus.Pending, BookingStatus.Accepted),
            (BookingStatus.Scheduled, BookingStatus.Accepted),
            (BookingStatus.Accepted, BookingStatus.InProgress),
            (BookingStatus.InProgress, BookingStatus.Completed),
            (BookingStatus.Pending, BookingStatus.Cancelled),
            (BookingStatus.Scheduled, BookingStatus.Cancelled),
            (BookingStatus.Accepted, BookingStatus.Cancelled)
        };

        foreach (var from in Enum.GetValues<BookingStatus>())
        {
            foreach (var to in Enum.GetValues<BookingStatus>())
            {
                if (from == to)
                    continue;

                if (allowed.Contains((from, to)))
                {
                    BookingStateMachine.CanTransition(from, to).Should().BeTrue($"{from} -> {to} is allowed");
                    var act = () => BookingStateMachine.Ensure(from, to);
                    act.Should().NotThrow();
                }
                else
                {
                    BookingStateMachine.CanTransition(from, to).Should().BeFalse($"{from} -> {to} is not allowed");
                    var act = () => BookingStateMachine.Ensure(from, to);
                    act.Should().Throw<ConflictException>();
                }
            }
        }
    }

    [Test]
    public async Task Schedule_AllowsExactlyTwoHoursAndRejectsEarlier()
    {
        await using var allowed = NewHarness();
        var onTheBoundary = await allowed.Workflow.ScheduleAsync(Customer(), Schedule(Now.AddHours(2)), CancellationToken.None);
        onTheBoundary.BookingDate.Should().Be(Now.AddHours(2));
        onTheBoundary.Status.Should().Be(nameof(BookingStatus.Scheduled));

        await using var rejected = NewHarness();
        var act = () => rejected.Workflow.ScheduleAsync(Customer(), Schedule(Now.AddHours(2).AddTicks(-1)), CancellationToken.None);
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.ToString().Should().Contain("2 hours");
    }

    [Test]
    public async Task TwoWashers_RaceOnScheduledBooking_OnlyOneWins()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "accept-race";
        await using (var seedDb = NewDb(name, root))
        {
            var seed = NewHarness(seedDb);
            var created = await seed.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
            var row = await seedDb.Bookings.SingleAsync();
            row.BookingType = BookingType.Scheduled;
            row.Status = BookingStatus.Scheduled;
            await seedDb.SaveChangesAsync();
            var first = NewHarness(NewDb(name, root));
            var second = NewHarness(NewDb(name, root));

            var attempts = await Task.WhenAll(
                Settle(() => first.Workflow.AcceptAsync(Washer(), created.BookingId, CancellationToken.None)),
                Settle(() => second.Workflow.AcceptAsync(OtherWasher(), created.BookingId, CancellationToken.None)));

            attempts.Count(attempt => attempt.Error is null).Should().Be(1);
            attempts.Count(attempt => attempt.Error is ConflictException).Should().Be(1);
            attempts.Single(attempt => attempt.Error is ConflictException).Error!.Message.Should().Contain("already assigned");
        }

        await using var check = NewDb(name, root);
        (await check.Bookings.SingleAsync()).WasherId.Should().NotBeNull();
    }

    [Test]
    public async Task Accept_RejectsOverlappingBookings_AndAllowsTheHalfOpenBoundary()
    {
        await using var harness = NewHarness();
        var first = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        var source = await harness.Db.Bookings.SingleAsync();
        var second = CopyPending(source, Now.AddMinutes(30), "GCW-20261121-000090");
        var adjacent = CopyPending(source, Now.AddMinutes(60), "GCW-20261121-000091");
        harness.Db.Bookings.AddRange(second, adjacent);
        await harness.Db.SaveChangesAsync();
        await SeedOffer(harness.Db, first.BookingId, 7);
        await harness.Workflow.AcceptAsync(Washer(), first.BookingId, CancellationToken.None);

        var overlap = () => harness.Workflow.AcceptAsync(Washer(), second.BookingId, CancellationToken.None);
        var rejected = await overlap.Should().ThrowAsync<ConflictException>();
        rejected.Which.Message.Should().Contain("overlap");

        var accepted = await harness.Workflow.AcceptAsync(Washer(), adjacent.BookingId, CancellationToken.None);
        accepted.Status.Should().Be(nameof(BookingStatus.Accepted));
    }

    [Test]
    public async Task Cancel_AfterInProgress_IsRejected()
    {
        await using var harness = NewHarness();
        var bookingId = await AcceptNewAsync(harness);
        await harness.Workflow.StartAsync(Washer(), bookingId, CancellationToken.None);

        var act = () => harness.Workflow.CancelAsync(Customer(), bookingId, new CancelBookingDto { Reason = "Too late" }, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
        (await harness.Db.Bookings.SingleAsync(booking => booking.BookingId == bookingId)).Status.Should().Be(BookingStatus.InProgress);
    }

    [Test]
    public async Task PromoRedeemFailure_CancelsTheBookingWithoutRelease()
    {
        await using var harness = NewHarness();
        harness.Catalog
            .Setup(catalog => catalog.RedeemAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("usage limit"));

        var act = () => harness.Workflow.WashNowAsync(Customer(), Request(promo: "welcome10"), CancellationToken.None);
        var rejected = await act.Should().ThrowAsync<ConflictException>();
        rejected.Which.Message.Should().Contain("Promo invalid");

        var stored = await harness.Db.Bookings.SingleAsync();
        stored.Status.Should().Be(BookingStatus.Cancelled);
        stored.CancelReason.Should().Be("Promo invalid");
        stored.CancelledBy.Should().Be(CancelledBy.System);
        (await harness.Db.OutboxEvents.Select(evt => evt.EventType).ToListAsync()).Should().Contain("BookingCancelled");
        harness.Catalog.Verify(catalog => catalog.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task PromoSaveFailure_AfterRedeem_ReleasesThePromo()
    {
        var root = new InMemoryDatabaseRoot();
        var interceptor = new FailNthSave { FailAt = 3 };
        await using var db = NewDb("promo-compensation", root, interceptor);
        var harness = NewHarness(db);

        var act = () => harness.Workflow.WashNowAsync(Customer(), Request(promo: "welcome10"), CancellationToken.None);
        await act.Should().ThrowAsync<IOException>();

        harness.Catalog.Verify(catalog => catalog.ReleaseAsync("WELCOME10", It.IsAny<CancellationToken>()), Times.Once);
        (await db.Bookings.SingleAsync()).Status.Should().Be(BookingStatus.Pending);
    }

    private static Booking CopyPending(Booking source, DateTime when, string number) => new()
    {
        BookingNumber = number,
        CustomerId = source.CustomerId,
        VehicleId = source.VehicleId,
        ServiceId = source.ServiceId,
        CityId = source.CityId,
        BookingType = BookingType.Scheduled,
        BookingDate = when,
        EstimatedDurationMin = source.EstimatedDurationMin,
        Address = source.Address,
        ServiceNameSnapshot = source.ServiceNameSnapshot,
        ServicePriceSnapshot = source.ServicePriceSnapshot,
        TotalAmount = source.TotalAmount,
        WaterSavedLiters = source.WaterSavedLiters,
        Status = BookingStatus.Pending,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static async Task<int> AcceptNewAsync(Harness harness)
    {
        var created = await harness.Workflow.WashNowAsync(Customer(), Request(), CancellationToken.None);
        await SeedOffer(harness.Db, created.BookingId, 7);
        await harness.Workflow.AcceptAsync(Washer(), created.BookingId, CancellationToken.None);
        return created.BookingId;
    }

    private static async Task SeedOffer(BookingDbContext db, int bookingId, int washerId)
    {
        db.BookingOffers.Add(new BookingOffer
        {
            BookingId = bookingId,
            WasherId = washerId,
            OfferedAt = Now,
            ExpiresAt = Now.AddMinutes(5),
            Response = OfferResponse.Pending
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(BookingDetailDto? Booking, Exception? Error)> Settle(Func<Task<BookingDetailDto>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, exception);
        }
    }

    private static Caller Customer() => new(3, "Customer");

    private static Caller Washer() => new(4, "Washer");

    private static Caller OtherWasher() => new(5, "Washer");

    private static CreateBookingDto Request(string? promo = null) => new()
    {
        VehicleId = 1,
        ServiceId = 1,
        AddOnIds = [1],
        PromoCode = promo
    };

    private static ScheduleBookingDto Schedule(DateTime when) => new()
    {
        VehicleId = 1,
        ServiceId = 1,
        AddOnIds = [],
        BookingDate = when
    };

    private static Harness NewHarness()
    {
        var root = new InMemoryDatabaseRoot();
        return NewHarness(NewDb(Guid.NewGuid().ToString("N"), root));
    }

    private static Harness NewHarness(
        BookingDbContext db,
        IPaymentCompensation? payments = null,
        IBookingRepository? repository = null)
    {
        var customers = new Mock<ICustomerDirectory>();
        customers.Setup(directory => directory.GetByUserAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerSnapshot { CustomerId = 1, UserId = 3, CityId = 1, Address = "1 Main St" });
        customers.Setup(directory => directory.GetVehicleAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VehicleSnapshot { VehicleId = 1, CustomerId = 1 });

        var washers = new Mock<IWasherDirectory>();
        washers.Setup(directory => directory.GetByUserAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WasherSnapshot { WasherId = 7, UserId = 4, CityId = 1 });
        washers.Setup(directory => directory.GetByUserAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WasherSnapshot { WasherId = 8, UserId = 5, CityId = 1 });
        washers.Setup(directory => directory.ConfirmAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        washers.Setup(directory => directory.StartAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        washers.Setup(directory => directory.CompleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        washers.Setup(directory => directory.ReleaseOfferAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        washers.Setup(directory => directory.MakeAvailableAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var catalog = new Mock<ICatalogPricing>();
        catalog.Setup(pricing => pricing.QuoteAsync(1, It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, IReadOnlyList<int> addOnIds, string? promo, CancellationToken _) => new QuoteSnapshot
            {
                ServiceNameSnapshot = "Basic",
                ServicePriceSnapshot = 19.99m,
                DiscountPct = 0,
                AddOnsTotal = addOnIds.Count == 0 ? 0m : 14.99m,
                AddOns = addOnIds.Count == 0
                    ? []
                    : [new QuoteAddOnSnapshot { Id = 1, Name = "Interior Vacuum", Price = 14.99m }],
                PromoDiscount = string.IsNullOrWhiteSpace(promo) ? 0m : 3.50m,
                TotalAmount = addOnIds.Count == 0
                    ? 19.99m
                    : (string.IsNullOrWhiteSpace(promo) ? 34.98m : 31.48m),
                DurationMin = 30,
                WaterSavedLiters = 15
            });
        catalog.Setup(pricing => pricing.RedeemAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        catalog.Setup(pricing => pricing.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cities = new Mock<IActiveCityCatalog>();
        cities.Setup(catalogCities => catalogCities.IsActiveAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var images = new Mock<IBookingImageStore>();
        images.Setup(store => store.SaveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/uploads/bookings/photo.jpg");

        var workflow = new BookingWorkflow(
            repository ?? new BookingRepository(db, new FixedIdentityOutbox()),
            customers.Object,
            washers.Object,
            catalog.Object,
            cities.Object,
            images.Object,
            new CreateBookingDtoValidator(),
            new ScheduleBookingDtoValidator(),
            new CancelBookingDtoValidator(),
            new OrderListQueryValidator(),
            NullDispatchQueue.Instance,
            payments ?? NullPaymentCompensation.Instance,
            new FakeClock(Now),
            NullLogger<BookingWorkflow>.Instance);

        return new Harness(db, workflow, catalog, washers);
    }

    private static BookingDbContext NewDb(string name, InMemoryDatabaseRoot root, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<BookingDbContext>();
        builder.UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new BookingDbContext(builder.Options);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(BookingDbContext db, BookingWorkflow workflow, Mock<ICatalogPricing> catalog, Mock<IWasherDirectory> washers)
        {
            Db = db;
            Workflow = workflow;
            Catalog = catalog;
            Washers = washers;
        }

        public BookingDbContext Db { get; }

        public BookingWorkflow Workflow { get; }

        public Mock<ICatalogPricing> Catalog { get; }

        public Mock<IWasherDirectory> Washers { get; }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class RefundHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _calls;
        private readonly string? _lookupStatus;
        private readonly HttpStatusCode _refundStatus;

        public RefundHandler(List<HttpRequestMessage> calls, string? lookupStatus, HttpStatusCode refundStatus)
        {
            _calls = calls;
            _lookupStatus = lookupStatus;
            _refundStatus = refundStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _calls.Add(request);
            if (request.Method == HttpMethod.Get)
            {
                if (_lookupStatus is null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var body = $"{{\"status\":\"{_lookupStatus}\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(_refundStatus)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FailNthSave : SaveChangesInterceptor
    {
        private int _count;

        public int FailAt { get; init; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _count++;
            if (_count >= FailAt)
                throw new IOException("save failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
