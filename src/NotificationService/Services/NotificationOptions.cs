namespace GreenCarWash.NotificationService.Services;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public int[] AdminUserIds { get; set; } = [1];

    public int MaxEmailAttempts { get; set; } = 5;

    public string PublicBaseUrl { get; set; } = "http://localhost:7004";
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseTls { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "noreply@greencarwash.example";
}
