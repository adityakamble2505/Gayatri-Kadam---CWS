using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Pagination;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.NotificationService.DTOs;
using GreenCarWash.NotificationService.Entities;
using GreenCarWash.NotificationService.Repositories;
using Microsoft.Extensions.Options;

namespace GreenCarWash.NotificationService.Services;

internal static class Input
{
    public static ValidationException Invalid(string property, string message) =>
        new(new[] { new ValidationFailure(property, message) });
}

public interface INotificationCenter
{
    Task AcceptAsync(NotificationEventDto dto, CancellationToken cancellationToken);

    Task<GreenCarWash.BuildingBlocks.Pagination.PagedResult<NotificationDto>> MyAsync(int userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken);

    Task<UnreadCountDto> UnreadCountAsync(int userId, CancellationToken cancellationToken);

    Task<NotificationDto> MarkReadAsync(int userId, long notificationId, CancellationToken cancellationToken);

    Task<UnreadCountDto> MarkAllReadAsync(int userId, CancellationToken cancellationToken);

    Task QueueEmailAsync(SendEmailDto dto, CancellationToken cancellationToken);

    Task<GreenCarWash.BuildingBlocks.Pagination.PagedResult<EmailLogDto>> EmailLogsAsync(string? status, int page, int pageSize, CancellationToken cancellationToken);

    Task<EmailLogDto> RetryEmailAsync(long emailLogId, CancellationToken cancellationToken);
}

public sealed class NotificationCenter : INotificationCenter
{
    private readonly INotificationRepository _notifications;
    private readonly IUserDirectory _users;
    private readonly IBookingNoticeDirectory _bookings;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public NotificationCenter(
        INotificationRepository notifications,
        IUserDirectory users,
        IBookingNoticeDirectory bookings,
        IOptions<NotificationOptions> options,
        IClock clock)
    {
        _notifications = notifications;
        _users = users;
        _bookings = bookings;
        _options = options.Value;
        _clock = clock;
    }

    public async Task AcceptAsync(NotificationEventDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.EventType))
            throw Input.Invalid("EventType", "eventType is required.");
        if (!EventNotificationMapper.IsKnown(dto.EventType))
            return;

        Validate(dto);
        var context = await BuildContextAsync(dto, cancellationToken);
        var drafts = EventNotificationMapper.Map(dto, context);
        foreach (var draft in drafts)
            await StoreAsync(draft, cancellationToken);
    }

    public async Task<GreenCarWash.BuildingBlocks.Pagination.PagedResult<NotificationDto>> MyAsync(int userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize, skip) = Pagination.Normalize(page, pageSize);
        var total = await _notifications.CountAsync(userId, unreadOnly, cancellationToken);
        var rows = await _notifications.ListAsync(userId, unreadOnly, skip, normalizedSize, cancellationToken);
        return new PagedResult<NotificationDto>
        {
            Items = rows.Select(ToDto).ToArray(),
            Page = normalizedPage,
            PageSize = normalizedSize,
            TotalCount = total
        };
    }

    public async Task<UnreadCountDto> UnreadCountAsync(int userId, CancellationToken cancellationToken) =>
        new() { UnreadCount = await _notifications.UnreadCountAsync(userId, cancellationToken) };

    public async Task<NotificationDto> MarkReadAsync(int userId, long notificationId, CancellationToken cancellationToken)
    {
        var updated = await _notifications.MarkReadAsync(notificationId, userId, _clock.UtcNow, cancellationToken);
        if (!updated)
            throw new NotFoundException("Notification", notificationId);
        var row = await _notifications.FindAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification", notificationId);
        return ToDto(row);
    }

    public async Task<UnreadCountDto> MarkAllReadAsync(int userId, CancellationToken cancellationToken)
    {
        await _notifications.MarkAllReadAsync(userId, _clock.UtcNow, cancellationToken);
        return new UnreadCountDto { UnreadCount = 0 };
    }

    public async Task QueueEmailAsync(SendEmailDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Subject) || dto.Subject.Length > 200)
            throw Input.Invalid("Subject", "Subject is required and must be at most 200 characters.");
        if (string.IsNullOrWhiteSpace(dto.Body))
            throw Input.Invalid("Body", "Body is required.");
        if (dto.ToUserId is not > 0 && string.IsNullOrWhiteSpace(dto.ToEmail))
            throw Input.Invalid("ToEmail", "toUserId or toEmail is required.");

        int? userId = dto.ToUserId is > 0 ? dto.ToUserId : null;
        var email = dto.ToEmail?.Trim() ?? string.Empty;
        var name = "there";
        if (userId is int id)
        {
            var contact = await _users.GetAsync(id, cancellationToken)
                ?? throw Input.Invalid("ToUserId", "User was not found.");
            if (!contact.IsActive || string.IsNullOrWhiteSpace(contact.Email))
                throw Input.Invalid("ToUserId", "User cannot receive email.");
            if (string.IsNullOrWhiteSpace(email))
                email = contact.Email;
            if (!string.IsNullOrWhiteSpace(contact.FullName))
                name = contact.FullName;
        }

        var html = EmailRenderer.Render("plain", new Dictionary<string, string>
        {
            ["name"] = name,
            ["message"] = dto.Body.Trim()
        });
        await _notifications.AddEmailAsync(new EmailLog
        {
            UserId = userId,
            ToEmail = email,
            Subject = dto.Subject.Trim(),
            Status = EmailStatus.Queued,
            CreatedAt = _clock.UtcNow
        }, html, cancellationToken);
    }

    public async Task<GreenCarWash.BuildingBlocks.Pagination.PagedResult<EmailLogDto>> EmailLogsAsync(string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize, skip) = Pagination.Normalize(page, pageSize);
        var (items, total) = await _notifications.EmailLogsAsync(status, skip, normalizedSize, cancellationToken);
        return new PagedResult<EmailLogDto>
        {
            Items = items.Select(ToEmail).ToArray(),
            Page = normalizedPage,
            PageSize = normalizedSize,
            TotalCount = total
        };
    }

    public async Task<EmailLogDto> RetryEmailAsync(long emailLogId, CancellationToken cancellationToken)
    {
        var row = await _notifications.RetryEmailAsync(emailLogId, cancellationToken)
            ?? throw new NotFoundException("Email", emailLogId);
        return ToEmail(row);
    }

    private async Task<NotificationMapContext> BuildContextAsync(NotificationEventDto dto, CancellationToken cancellationToken)
    {
        var context = new NotificationMapContext { AdminUserIds = _options.AdminUserIds ?? [] };
        if (dto.EventType is "PaymentSucceeded" or "BookingCancelled")
        {
            var booking = await _bookings.GetAsync(dto.BookingId, cancellationToken);
            if (dto.EventType == "PaymentSucceeded")
            {
                context = new NotificationMapContext
                {
                    AdminUserIds = context.AdminUserIds,
                    Receipt = new ReceiptDetails
                    {
                        Package = booking.ServiceName,
                        AddOnLines = booking.AddOns.Select(addOn => addOn.Name + " " + addOn.Price.ToString("0.00", CultureInfo.InvariantCulture)).ToArray(),
                        Total = booking.TotalAmount,
                        PhotoUrl = Absolute(booking.AfterWashPhotoUrl)
                    }
                };
            }
            else
            {
                context = new NotificationMapContext
                {
                    AdminUserIds = context.AdminUserIds,
                    CancelledBy = booking.CancelledBy
                };
            }
        }

        return context;
    }

    private async Task StoreAsync(NotificationDraft draft, CancellationToken cancellationToken)
    {
        var contact = await _users.GetAsync(draft.UserId, cancellationToken);
        var active = contact is { IsActive: true } && !string.IsNullOrWhiteSpace(contact.Email);
        EmailLog? email = null;
        string? html = null;
        if (draft.SendEmail && active)
        {
            var name = string.IsNullOrWhiteSpace(contact!.FullName) ? "there" : contact.FullName;
            html = EmailRenderer.Compose(draft, name);
            email = new EmailLog
            {
                UserId = draft.UserId,
                ToEmail = contact.Email,
                Subject = Cut(draft.Title, 200),
                Status = EmailStatus.Queued,
                CreatedAt = _clock.UtcNow
            };
        }

        await _notifications.AddOnceAsync(new Notification
        {
            UserId = draft.UserId,
            Type = draft.Type,
            Title = Cut(draft.Title, 150),
            Message = Cut(draft.Message, 1000),
            ReferenceType = draft.ReferenceType,
            ReferenceId = draft.ReferenceId,
            CreatedAt = _clock.UtcNow
        }, email, html, cancellationToken);
    }

    private static void Validate(NotificationEventDto dto)
    {
        if (dto.BookingId <= 0)
            throw Input.Invalid("BookingId", "bookingId is required.");
        if (string.IsNullOrWhiteSpace(dto.BookingNumber))
            throw Input.Invalid("BookingNumber", "bookingNumber is required.");
        if (string.IsNullOrWhiteSpace(dto.ReferenceId))
            throw Input.Invalid("ReferenceId", "referenceId is required.");
        if (dto.OccurredAt == default)
            throw Input.Invalid("OccurredAt", "occurredAt is required.");
        if (dto.CustomerUserId <= 0)
            throw Input.Invalid("CustomerUserId", "customerUserId is required.");
    }

    private string? Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        return _options.PublicBaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
    }

    private static string Cut(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static NotificationDto ToDto(Notification row) => new()
    {
        NotificationId = row.NotificationId,
        Type = row.Type,
        Title = row.Title,
        Message = row.Message,
        ReferenceType = row.ReferenceType,
        ReferenceId = row.ReferenceId,
        IsRead = row.IsRead,
        ReadAt = row.ReadAt,
        CreatedAt = row.CreatedAt
    };

    private static EmailLogDto ToEmail(EmailLog row) => new()
    {
        EmailLogId = row.EmailLogId,
        UserId = row.UserId,
        ToEmail = row.ToEmail,
        Subject = row.Subject,
        Status = row.Status.ToString(),
        Attempts = row.Attempts,
        LastError = row.LastError,
        CreatedAt = row.CreatedAt,
        SentAt = row.SentAt
    };
}
