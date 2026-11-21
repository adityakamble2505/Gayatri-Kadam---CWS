namespace GreenCarWash.NotificationService.DTOs;

public sealed class NotificationEventDto
{
    public string? EventType { get; set; }

    public int BookingId { get; set; }

    public string? BookingNumber { get; set; }

    public int CustomerUserId { get; set; }

    public int? WasherUserId { get; set; }

    public string? Status { get; set; }

    public string? ReferenceId { get; set; }

    public DateTime OccurredAt { get; set; }

    public decimal? Amount { get; set; }

    public string? Reason { get; set; }
}

public sealed class NotificationDto
{
    public long NotificationId { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? ReferenceType { get; init; }

    public int? ReferenceId { get; init; }

    public bool IsRead { get; init; }

    public DateTime? ReadAt { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class UnreadCountDto
{
    public int UnreadCount { get; init; }
}

public sealed class SendEmailDto
{
    public int? ToUserId { get; set; }

    public string? ToEmail { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}

public sealed class EmailLogDto
{
    public long EmailLogId { get; init; }

    public int? UserId { get; init; }

    public string ToEmail { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int Attempts { get; init; }

    public string? LastError { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? SentAt { get; init; }
}
