using System.Collections.Concurrent;
using FluentAssertions;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.PaymentService.Data;
using GreenCarWash.PaymentService.DTOs;
using GreenCarWash.PaymentService.Entities;
using GreenCarWash.PaymentService.Repositories;
using GreenCarWash.PaymentService.Services;
using GreenCarWash.PaymentService.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.PaymentService.Tests;

[NonParallelizable]
public sealed class PaymentFlowTests
{
    private const string ApproveCard = "4111111111111111";
    private const string DeclineCard = "4000000000000002";
    private const string TimeoutCard = "4111111111119999";
    private const string Cvv = "9087";
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TC_17_SuccessfulPayment_ChargesTheBookingTotal()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM-OK"));
        await using var db = NewDb();
        var result = await Processor(db, gateway, Booking()).ProcessAsync(Customer(), Pay(1, ApproveCard, "pay-1"), CancellationToken.None);

        result.Replay.Should().BeFalse();
        result.Payment.Status.Should().Be("Success");
        result.Payment.Amount.Should().Be(31.48m);
        result.Payment.CardLast4.Should().Be("1111");
        result.Payment.CardBrand.Should().Be("Visa");
        result.Payment.TransactionReference.Should().Be("SIM-OK");
        gateway.Calls.Should().Be(1);
        (await db.OutboxEvents.SingleAsync()).EventType.Should().Be("PaymentSucceeded");
    }

    [Test]
    public async Task TC_18_InvalidAmountOrOwnership_IsRejected()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM-OK"));
        await using var db = NewDb();
        var owned = Processor(db, gateway, Booking());
        var act = () => owned.ProcessAsync(new Caller(9, "Customer"), Pay(1, ApproveCard, "other"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();

        var zero = Processor(db, gateway, Booking(total: 0));
        var amount = () => zero.ProcessAsync(Customer(), Pay(1, ApproveCard, "zero"), CancellationToken.None);
        await amount.Should().ThrowAsync<ValidationException>();
        gateway.Calls.Should().Be(0);
    }

    [Test]
    public async Task TC_19_DuplicatePayment_IsConflict()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM-OK"));
        await using var db = NewDb();
        var processor = Processor(db, gateway, Booking());
        await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "first"), CancellationToken.None);

        var act = () => processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "second"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        gateway.Calls.Should().Be(1);
    }

    [Test]
    public async Task SameIdempotencyKey_ChargesOnce()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM-OK"));
        await using var db = NewDb();
        var processor = Processor(db, gateway, Booking());
        var first = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "same"), CancellationToken.None);
        var second = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "same"), CancellationToken.None);

        second.Replay.Should().BeTrue();
        second.Payment.PaymentId.Should().Be(first.Payment.PaymentId);
        gateway.Calls.Should().Be(1);
    }

    [Test]
    public async Task TwentyParallelCharges_ForOneBooking_ChargeOnce()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "pay-race";
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM-" + Guid.NewGuid().ToString("N")));
        var outcomes = new ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 20).Select(async index =>
        {
            await using var db = NewDb(name, root);
            try
            {
                await Processor(db, gateway, Booking()).ProcessAsync(Customer(), Pay(1, ApproveCard, "race-" + index), CancellationToken.None);
                outcomes.Add("paid");
            }
            catch (ConflictException)
            {
                outcomes.Add("conflict");
            }
        });

        await Task.WhenAll(tasks);

        outcomes.Count(outcome => outcome == "paid").Should().Be(1);
        gateway.Calls.Should().Be(1);
        await using var check = NewDb(name, root);
        (await check.Payments.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task FailedPayment_CanBeRetriedOnTheSameRow()
    {
        await using var db = NewDb();
        var gateway = new SimulatedGatewayService(new NoDelay());
        var processor = Processor(db, gateway, Booking());
        var declined = await processor.ProcessAsync(Customer(), Pay(1, DeclineCard, "try-1"), CancellationToken.None);
        var paid = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "try-2"), CancellationToken.None);

        paid.Payment.PaymentId.Should().Be(declined.Payment.PaymentId);
        paid.Payment.Status.Should().Be("Success");
        (await db.Payments.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task DeclinedCard_IsStoredAsFailed()
    {
        await using var db = NewDb();
        var result = await Processor(db, new SimulatedGatewayService(new NoDelay()), Booking())
            .ProcessAsync(Customer(), Pay(1, DeclineCard, "decline"), CancellationToken.None);

        result.Payment.Status.Should().Be("Failed");
        result.Payment.FailureReason.Should().Be("Card declined");
        (await db.OutboxEvents.SingleAsync()).EventType.Should().Be("PaymentFailed");
    }

    [Test]
    public async Task TimeoutCard_FailsTheRowAndThrowsGatewayTimeout()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Timeout());
        await using var db = NewDb();
        var act = () => Processor(db, gateway, Booking()).ProcessAsync(Customer(), Pay(1, TimeoutCard, "timeout"), CancellationToken.None);

        await act.Should().ThrowAsync<GatewayTimeoutException>();
        gateway.Calls.Should().Be(1);
        var stored = await db.Payments.SingleAsync();
        stored.PaymentStatus.Should().Be(PaymentStatus.Failed);
        stored.FailureReason.Should().Be("Gateway timed out");
    }

    [Test]
    public async Task LuhnAndExpiry_AreRejectedBeforeTheGateway()
    {
        var gateway = new ScriptedGateway(_ => GatewayResult.Approve("SIM"));
        await using var db = NewDb();
        var processor = Processor(db, gateway, Booking());

        var luhn = () => processor.ProcessAsync(Customer(), Pay(1, "4111111111111112", "bad-luhn"), CancellationToken.None);
        var expiry = () => processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "old-card", month: 10, year: 2026), CancellationToken.None);

        (await luhn.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(error => error.ErrorMessage == "Card number is invalid.");
        (await expiry.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(error => error.ErrorMessage == "Card expiry is in the past.");
        gateway.Calls.Should().Be(0);
    }

    [Test]
    public async Task PendingReaper_AbandonsPaymentsOlderThanFiveMinutes()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "reaper";
        await using (var seed = NewDb(name, root))
        {
            seed.Payments.Add(Pending("old", Now.AddMinutes(-6)));
            seed.Payments.Add(Pending("fresh", Now.AddMinutes(-1)));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PaymentDbContext>(builder => builder
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        await using var provider = services.BuildServiceProvider();
        var reaper = new PendingReaper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentOptions()),
            new FixedClock(Now),
            NullLogger<PendingReaper>.Instance);

        (await reaper.AbandonAsync(CancellationToken.None)).Should().Be(1);
        await using var check = NewDb(name, root);
        var rows = await check.Payments.AsNoTracking().OrderBy(payment => payment.PaymentId).ToListAsync();
        rows[0].PaymentStatus.Should().Be(PaymentStatus.Failed);
        rows[0].FailureReason.Should().Be("Abandoned");
        rows[1].PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    [Test]
    public async Task PartialRefund_UpdatesTheRemainingBalance()
    {
        await using var db = NewDb();
        var processor = Processor(db, new ScriptedGateway(_ => GatewayResult.Approve("SIM")), Booking());
        var paid = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "paid"), CancellationToken.None);
        var refund = await processor.RefundAsync(new Caller(1, "Admin"), new RefundRequestDto
        {
            PaymentId = paid.Payment.PaymentId,
            Amount = 10m,
            Reason = "Goodwill"
        }, CancellationToken.None);

        refund.RefundId.Should().BeGreaterThan(0);
        refund.Status.Should().Be("PartiallyRefunded");
        refund.Amount.Should().Be(10m);
        refund.RefundedAmount.Should().Be(10m);
        (await db.PaymentRefunds.SingleAsync()).Amount.Should().Be(10m);
    }

    [Test]
    public async Task OverRefund_IsRejected()
    {
        await using var db = NewDb();
        var processor = Processor(db, new ScriptedGateway(_ => GatewayResult.Approve("SIM")), Booking());
        var paid = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "paid"), CancellationToken.None);
        var act = () => processor.RefundAsync(new Caller(1, "Admin"), new RefundRequestDto
        {
            PaymentId = paid.Payment.PaymentId,
            Amount = 40m,
            Reason = "Too much"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await db.Payments.SingleAsync()).RefundedAmount.Should().Be(0);
    }

    [Test]
    public async Task ParallelRefunds_NeverExceedTheAmount()
    {
        await using var db = NewDb();
        var processor = Processor(db, new ScriptedGateway(_ => GatewayResult.Approve("SIM")), Booking(total: 10m));
        var paid = await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "paid"), CancellationToken.None);
        var tasks = Enumerable.Range(0, 8).Select(_ => processor.RefundAsync(new Caller(1, "Admin"), new RefundRequestDto
        {
            PaymentId = paid.Payment.PaymentId,
            Amount = 2m,
            Reason = "Slice"
        }, CancellationToken.None));

        var results = await Task.WhenAll(tasks.Select(async task =>
        {
            try
            {
                await task;
                return true;
            }
            catch (ValidationException)
            {
                return false;
            }
            catch (ConflictException)
            {
                return false;
            }
        }));

        results.Count(applied => applied).Should().Be(5);
        var stored = await db.Payments.SingleAsync();
        stored.RefundedAmount.Should().Be(10m);
        stored.RefundedAmount.Should().BeLessThanOrEqualTo(stored.Amount);
        (await db.PaymentRefunds.SumAsync(refund => refund.Amount)).Should().Be(10m);
    }

    [Test]
    public async Task Receipt_RequiresACompletedBookingAndASuccessfulPayment()
    {
        await using var db = NewDb();
        var processor = Processor(db, new ScriptedGateway(_ => GatewayResult.Approve("SIM")), Booking(status: "Accepted"));
        await processor.ProcessAsync(Customer(), Pay(1, ApproveCard, "paid"), CancellationToken.None);
        var early = () => processor.ReceiptAsync(Customer(), 1, CancellationToken.None);
        await early.Should().ThrowAsync<ConflictException>();

        var completed = Processor(db, new ScriptedGateway(_ => GatewayResult.Approve("SIM")), Booking());
        var receipt = await completed.ReceiptAsync(Customer(), 1, CancellationToken.None);
        receipt.BookingNumber.Should().Be("GCW-20261121-000001");
        receipt.Package.Should().Be("Basic");
        receipt.Total.Should().Be(31.48m);
        receipt.AfterWashPhotoUrl.Should().Be("/uploads/bookings/photo.jpg");
    }

    [Test]
    public async Task Logs_DoNotContainTheCardNumberOrCvv()
    {
        var logs = new ListLogger();
        await using var db = NewDb();
        await Processor(db, new SimulatedGatewayService(new NoDelay()), Booking(), logs)
            .ProcessAsync(Customer(), Pay(1, ApproveCard, "logged"), CancellationToken.None);

        var output = string.Join('\n', logs.Lines);
        output.Should().NotContain(ApproveCard);
        output.Should().NotContain(Cvv);
    }

    [Test]
    public async Task OutboxBatch_StopsAfterTheFirstDeliveryOutage()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "outbox-stop";
        await using (var seed = NewDb(name, root))
        {
            for (var index = 0; index < 20; index++)
            {
                seed.OutboxEvents.Add(new OutboxEvent
                {
                    EventType = "PaymentSucceeded",
                    Payload = "{}",
                    CreatedAt = Now,
                    NextAttemptAt = Now
                });
            }

            await seed.SaveChangesAsync();
        }

        var publisher = new Mock<IOutboxEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PaymentDbContext>(builder => builder
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddSingleton(publisher.Object);
        await using var provider = services.BuildServiceProvider();
        var hosted = new OutboxPublisherBackgroundService<PaymentDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            NullLogger<OutboxPublisherBackgroundService<PaymentDbContext>>.Instance);

        await hosted.PublishBatchAsync(CancellationToken.None);

        publisher.Verify(item => item.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var check = NewDb(name, root);
        var rows = await check.OutboxEvents.AsNoTracking().OrderBy(evt => evt.EventId).ToListAsync();
        rows[0].RetryCount.Should().Be(1);
        rows.Skip(1).Should().OnlyContain(evt => evt.RetryCount == 0 && evt.NextAttemptAt == Now);
    }

    private static PaymentProcessor Processor(
        PaymentDbContext db,
        IPaymentGateway gateway,
        BookingPaymentSnapshot booking,
        ILogger<PaymentProcessor>? logger = null)
    {
        var customers = new Mock<ICustomerDirectory>();
        customers.Setup(directory => directory.GetByUserAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerPaymentSnapshot { CustomerId = 1, UserId = 3 });
        customers.Setup(directory => directory.GetByUserAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerPaymentSnapshot { CustomerId = 2, UserId = 9 });
        var bookings = new Mock<IBookingDirectory>();
        bookings.Setup(directory => directory.GetAsync(booking.BookingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(booking);
        return new PaymentProcessor(
            new PaymentRepository(db),
            bookings.Object,
            customers.Object,
            new Mock<IWasherDirectory>().Object,
            gateway,
            new ProcessPaymentDtoValidator(new FixedClock(Now)),
            new RefundRequestDtoValidator(),
            Options.Create(new PaymentOptions()),
            new FixedClock(Now),
            logger ?? NullLogger<PaymentProcessor>.Instance);
    }

    private static BookingPaymentSnapshot Booking(decimal total = 31.48m, string status = "Completed") => new()
    {
        BookingId = 1,
        BookingNumber = "GCW-20261121-000001",
        Status = status,
        TotalAmount = total,
        CustomerId = 1,
        CustomerUserId = 3,
        WasherId = 1,
        WasherUserId = 4,
        ServiceName = "Basic",
        AfterWashPhotoUrl = "/uploads/bookings/photo.jpg",
        AddOns = [new BookingAddOnSnapshot { AddOnId = 1, Name = "Interior Vacuum", Price = 14.99m }]
    };

    private static ProcessPaymentDto Pay(int bookingId, string card, string key, int month = 12, int year = 2027) => new()
    {
        BookingId = bookingId,
        CardNumber = card,
        ExpiryMonth = month,
        ExpiryYear = year,
        Cvv = Cvv,
        IdempotencyKey = key
    };

    private static Caller Customer() => new(3, "Customer");

    private static Payment Pending(string key, DateTime updated) => new()
    {
        BookingId = key == "old" ? 1 : 2,
        CustomerId = 1,
        Amount = 10m,
        Currency = "USD",
        PaymentMethod = PaymentMethod.Card,
        PaymentStatus = PaymentStatus.Pending,
        IdempotencyKey = key,
        CreatedAt = updated,
        UpdatedAt = updated
    };

    private static PaymentDbContext NewDb() => NewDb(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());

    private static PaymentDbContext NewDb(string name, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new PaymentDbContext(options);
    }

    private sealed class ScriptedGateway : IPaymentGateway
    {
        private readonly Func<string, GatewayResult> _next;

        public ScriptedGateway(Func<string, GatewayResult> next)
        {
            _next = next;
        }

        public int Calls { get; private set; }

        public Task<GatewayResult> ChargeAsync(string cardLast4, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_next(cardLast4));
        }
    }

    private sealed class NoDelay : IGatewayDelay
    {
        public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class ListLogger : ILogger<PaymentProcessor>
    {
        public List<string> Lines { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null)
                Lines.Add(exception.ToString());
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
