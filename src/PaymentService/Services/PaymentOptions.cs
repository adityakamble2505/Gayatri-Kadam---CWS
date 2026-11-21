namespace GreenCarWash.PaymentService.Services;

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    public string[] PayableStatuses { get; set; } =
    [
        "Pending",
        "Scheduled",
        "Accepted",
        "InProgress",
        "Completed"
    ];

    public int GatewayMinDelayMs { get; set; } = 30;

    public int GatewayMaxDelayMs { get; set; } = 120;

    public int PendingTimeoutMinutes { get; set; } = 5;
}
