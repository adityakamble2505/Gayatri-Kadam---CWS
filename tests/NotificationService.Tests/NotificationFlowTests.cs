using FluentAssertions;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.NotificationService.Data;
using GreenCarWash.NotificationService.DTOs;
using GreenCarWash.NotificationService.Entities;
using GreenCarWash.NotificationService.Repositories;
using GreenCarWash.NotificationService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.NotificationService.Tests;

[NonParallelizable]
public sealed class NotificationFlowTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private const string CustomerEmail = "customer@example.com";
    private const string ScriptName = "Ada <script>";

    [Test]
    public async Task TC_23_BookingCreated_NotifiesTheCustomerAndQueuesEmail()
    {
        await using var db = NewDb();
        var center = Center(db, Contact(3, ScriptName, CustomerEmail, true));
        await center.AcceptAsync(Event("BookingCreated"), CancellationToken.None);

        var notice = await db.Notifications.SingleAsync();
        notice.UserId.Should().Be(3);
        notice.Type.Should().Be(EventNotificationMapper.Booking);
        notice.Title.Should().Be("Booking received");
        (await db.EmailLogs.SingleAsync()).Status.Should().Be(EmailStatus.Queued);
        (await center.UnreadCountAsync(3, CancellationToken.None)).UnreadCount.Should().Be(1);
    }

    [Test]
    public async Task TC_24_MarkRead_IsOwnerOnly_AndReadAllClearsUnread()
    {
        await using var db = NewDb();
        var center = Center(db, Contact(3, "Ada", CustomerEmail, true));
        await center.AcceptAsync(Event("BookingCreated"), CancellationToken.None);
        await center.AcceptAsync(Event("ServiceStarted"), CancellationToken.None);
        var first = await db.Notifications.OrderBy(row => row.NotificationId).FirstAsync();

        var other = () => center.MarkReadAsync(9, first.NotificationId, CancellationToken.None);
        await other.Should().ThrowAsync<NotFoundException>();

        var read = await center.MarkReadAsync(3, first.NotificationId, CancellationToken.None);
        read.IsRead.Should().BeTrue();
        (await center.UnreadCountAsync(3, CancellationToken.None)).UnreadCount.Should().Be(1);

        (await center.MarkAllReadAsync(3, CancellationToken.None)).UnreadCount.Should().Be(0);
        (await db.Notifications.CountAsync(row => !row.IsRead)).Should().Be(0);
    }

    [Test]
    public void Mapper_CoversEveryEvent_AndSkipsANullWasher()
    {
        var context = new NotificationMapContext
        {
            AdminUserIds = [1],
            CancelledBy = "Customer",
            Receipt = new ReceiptDetails { Package = "Basic", AddOnLines = ["Vacuum 14.99"], Total = 31.48m, PhotoUrl = "http://localhost:7004/uploads/photo.jpg" }
        };

        Recipients("BookingCreated", context).Should().Equal((3, EventNotificationMapper.Booking, true));
        Recipients("WashRequest", context).Should().Equal((4, EventNotificationMapper.WashRequest, false));
        Recipients("WashRequest", context, washer: null).Should().BeEmpty();
        Recipients("WasherAccepted", context).Should().Equal((3, EventNotificationMapper.Accepted, true));
        Recipients("ServiceStarted", context).Should().Equal((3, EventNotificationMapper.Started, true));
        Recipients("ServiceCompleted", context).Should().Equal((3, EventNotificationMapper.Completed, true));
        Recipients("PaymentSucceeded", context).Should().Equal(
            (3, EventNotificationMapper.Receipt, true),
            (4, EventNotificationMapper.Payment, false));
        Recipients("PaymentSucceeded", context, washer: null).Should().Equal((3, EventNotificationMapper.Receipt, true));
        Recipients("PaymentFailed", context).Should().Equal((3, EventNotificationMapper.Payment, true));
        Recipients("PaymentRefunded", context).Should().Equal((3, EventNotificationMapper.Payment, true));
        Recipients("Reminder2h", context).Should().Equal((4, EventNotificationMapper.Reminder, true));
        Recipients("Reminder2h", context, washer: null).Should().BeEmpty();
        Recipients("BookingCancelled", context).Should().Equal((4, EventNotificationMapper.Cancelled, true));
        Recipients("BookingCancelled", context, washer: null).Should().BeEmpty();
        Recipients("BookingCancelled", new NotificationMapContext { CancelledBy = "Washer" }).Should().Equal((3, EventNotificationMapper.Cancelled, true));
        Recipients("BookingCancelled", new NotificationMapContext { CancelledBy = "Admin" }).Should().Equal(
            (3, EventNotificationMapper.Cancelled, true),
            (4, EventNotificationMapper.Cancelled, true));
        Recipients("BookingCancelled", new NotificationMapContext { CancelledBy = "Admin" }, washer: null)
            .Should().Equal((3, EventNotificationMapper.Cancelled, true));
        Recipients("AdminAlert", context).Should().Equal((1, EventNotificationMapper.AdminAlert, true));
        Recipients("RefundPending", context).Should().Equal((1, EventNotificationMapper.AdminAlert, true));
        EventNotificationMapper.Map(Event("RatingSyncPending"), context).Should().BeEmpty();
    }

    [Test]
    public async Task DuplicateEvent_CreatesOneNotification()
    {
        await using var db = NewDb();
        var center = Center(db, Contact(3, "Ada", CustomerEmail, true));
        await center.AcceptAsync(Event("BookingCreated"), CancellationToken.None);
        await center.AcceptAsync(Event("BookingCreated"), CancellationToken.None);

        (await db.Notifications.CountAsync()).Should().Be(1);
        (await db.EmailLogs.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task FiftyParallelEvents_CreateOneNotification()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "parallel-events";
        var store = new EmailContentStore();
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb(name, root);
            await Center(db, Contact(3, "Ada", CustomerEmail, true), store).AcceptAsync(Event("BookingCreated"), CancellationToken.None);
        }));

        await Task.WhenAll(tasks);
        await using var check = NewDb(name, root);
        (await check.Notifications.CountAsync()).Should().Be(1);
        (await check.EmailLogs.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task UnknownEvent_ReturnsWithoutCreatingAnything()
    {
        await using var db = NewDb();
        var center = Center(db, Contact(3, "Ada", CustomerEmail, true));
        await center.AcceptAsync(Event("RatingSyncPending"), CancellationToken.None);
        (await db.Notifications.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task InvalidPayload_IsRejected()
    {
        await using var db = NewDb();
        var center = Center(db, Contact(3, "Ada", CustomerEmail, true));
        var act = () => center.AcceptAsync(new NotificationEventDto { EventType = "BookingCreated" }, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task SmtpFailure_ThenRetry_ThenSuccess_AndMaxAttemptsFail()
    {
        await using var db = NewDb();
        var store = new EmailContentStore();
        var smtp = new ScriptSmtp(_ => throw new InvalidOperationException("smtp down"));
        var repo = new NotificationRepository(db, store);
        db.EmailLogs.Add(Queued("one@example.com", Now.AddDays(-1)));
        await db.SaveChangesAsync();
        store.Put(1, "<p>body</p>");

        await repo.DeliverBatchAsync(smtp, NullLogger.Instance, Now, 5, CancellationToken.None);
        (await db.EmailLogs.SingleAsync()).Attempts.Should().Be(1);
        (await db.EmailLogs.SingleAsync()).Status.Should().Be(EmailStatus.Queued);

        smtp.Next = _ => Task.CompletedTask;
        await repo.DeliverBatchAsync(smtp, NullLogger.Instance, Now, 5, CancellationToken.None);
        (await db.EmailLogs.SingleAsync()).Status.Should().Be(EmailStatus.Sent);

        db.EmailLogs.Add(Queued("two@example.com", Now.AddDays(-1)));
        await db.SaveChangesAsync();
        var second = await db.EmailLogs.OrderByDescending(row => row.EmailLogId).FirstAsync();
        store.Put(second.EmailLogId, "<p>body</p>");
        smtp.Next = _ => throw new InvalidOperationException("smtp down");
        for (var attempt = 0; attempt < 5; attempt++)
            await repo.DeliverBatchAsync(smtp, NullLogger.Instance, Now, 5, CancellationToken.None);

        (await db.EmailLogs.SingleAsync(row => row.EmailLogId == second.EmailLogId)).Status.Should().Be(EmailStatus.Failed);
    }

    [Test]
    public void Html_EncodesScriptInTheName()
    {
        var html = EmailRenderer.Render("booking-received", new Dictionary<string, string>
        {
            ["name"] = ScriptName,
            ["bookingNumber"] = "GCW-1"
        });

        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>");
    }

    [Test]
    public async Task Logs_DoNotContainTheEmailBodyOrAddress()
    {
        await using var db = NewDb();
        var store = new EmailContentStore();
        var logs = new ListLogger();
        var repo = new NotificationRepository(db, store);
        await Center(db, Contact(3, ScriptName, CustomerEmail, true), store).AcceptAsync(Event("BookingCreated"), CancellationToken.None);
        await repo.DeliverBatchAsync(new ScriptSmtp(_ => Task.CompletedTask), logs, Now, 5, CancellationToken.None);

        var output = string.Join('\n', logs.Lines);
        output.Should().NotContain(CustomerEmail);
        output.Should().NotContain("we received booking");
        output.Should().NotContain(ScriptName);
        output.Should().Contain("c***@example.com");
    }

    [Test]
    public async Task AuthDown_IsServiceUnavailable()
    {
        await using var db = NewDb();
        var users = new Mock<IUserDirectory>();
        users.Setup(directory => directory.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceUnavailableException("Auth service is unavailable."));
        var center = Center(db, users.Object);
        var act = () => center.AcceptAsync(Event("BookingCreated"), CancellationToken.None);
        await act.Should().ThrowAsync<ServiceUnavailableException>();
        (await db.Notifications.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task TwoEmailWorkers_DoNotSendTheSameRow()
    {
        var root = new InMemoryDatabaseRoot();
        const string name = "email-workers";
        var store = new EmailContentStore();
        await using (var seed = NewDb(name, root))
        {
            seed.EmailLogs.Add(Queued("washer@example.com", Now));
            await seed.SaveChangesAsync();
            store.Put(1, "<p>once</p>");
        }

        var smtp = new ScriptSmtp(async _ => await Task.Delay(40));
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb(name, root);
            var repo = new NotificationRepository(db, store);
            await repo.DeliverBatchAsync(smtp, NullLogger.Instance, Now, 5, CancellationToken.None);
        }));
        await Task.WhenAll(tasks);

        smtp.Calls.Should().Be(1);
        await using var check = NewDb(name, root);
        (await check.EmailLogs.SingleAsync()).Status.Should().Be(EmailStatus.Sent);
    }

    private static IReadOnlyList<(int UserId, string Type, bool Email)> Recipients(string eventType, NotificationMapContext context, int? washer = 4) =>
        EventNotificationMapper.Map(Event(eventType, washer), context)
            .Select(draft => (draft.UserId, draft.Type, draft.SendEmail))
            .ToArray();

    private static NotificationCenter Center(NotificationDbContext db, UserContact contact, IEmailContentStore? store = null)
    {
        var users = new Mock<IUserDirectory>();
        users.Setup(directory => directory.GetAsync(contact.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        users.Setup(directory => directory.GetAsync(It.Is<int>(id => id != contact.UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => Contact(id, "Person", "person@example.com", true));
        return Center(db, users.Object, store);
    }

    private static NotificationCenter Center(NotificationDbContext db, IUserDirectory users, IEmailContentStore? store = null)
    {
        var bookings = new Mock<IBookingNoticeDirectory>();
        bookings.Setup(directory => directory.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingNotice("Basic", [new BookingAddOnNotice("Vacuum", 14.99m)], 31.48m, "/uploads/photo.jpg", "Customer"));
        return new NotificationCenter(
            new NotificationRepository(db, store ?? new EmailContentStore()),
            users,
            bookings.Object,
            Options.Create(new NotificationOptions { AdminUserIds = [1], PublicBaseUrl = "http://localhost:7004" }),
            new FixedClock(Now));
    }

    private static NotificationEventDto Event(string eventType, int? washer = 4) => new()
    {
        EventType = eventType,
        BookingId = 8,
        BookingNumber = "GCW-20261121-000008",
        CustomerUserId = 3,
        WasherUserId = washer,
        Status = "Pending",
        ReferenceId = "8",
        OccurredAt = Now,
        Amount = 31.48m,
        Reason = "Needs attention"
    };

    private static UserContact Contact(int id, string name, string email, bool active) => new(id, name, email, active);

    private static EmailLog Queued(string email, DateTime created) => new()
    {
        ToEmail = email,
        Subject = "Hello",
        Status = EmailStatus.Queued,
        CreatedAt = created
    };

    private static NotificationDbContext NewDb() => NewDb(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());

    private static NotificationDbContext NewDb(string name, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(name, root)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new NotificationDbContext(options);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class ScriptSmtp : ISmtpEmailService
    {
        public ScriptSmtp(Func<string, Task> next)
        {
            Next = next;
        }

        public Func<string, Task> Next { get; set; }

        private int _calls;

        public int Calls => _calls;

        public Task SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Next(toEmail);
        }
    }

    private sealed class ListLogger : ILogger
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
            public void Dispose() { }
        }
    }
}
