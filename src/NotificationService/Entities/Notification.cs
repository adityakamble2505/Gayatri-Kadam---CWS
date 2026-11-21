namespace GreenCarWash.NotificationService.Entities;

public sealed class Notification
{
    public long NotificationId { get; set; }

    public int UserId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? ReferenceType { get; set; }

    public int? ReferenceId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum EmailStatus
{
    Queued,
    Sent,
    Failed
}

public sealed class EmailLog
{
    public long EmailLogId { get; set; }

    public int? UserId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public EmailStatus Status { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }
}
