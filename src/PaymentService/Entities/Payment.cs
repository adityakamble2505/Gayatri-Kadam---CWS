namespace GreenCarWash.PaymentService.Entities;

public enum PaymentStatus
{
    Pending,
    Success,
    Failed,
    Refunded,
    PartiallyRefunded
}

public enum PaymentMethod
{
    Card,
    Simulated
}

public sealed class Payment
{
    public int PaymentId { get; set; }

    public int BookingId { get; set; }

    public int CustomerId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public PaymentMethod PaymentMethod { get; set; }

    public string? CardBrand { get; set; }

    public string? CardLast4 { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public string? TransactionReference { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    public decimal RefundedAmount { get; set; }

    public DateTime? PaymentDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class PaymentRefund
{
    public int RefundId { get; set; }

    public int PaymentId { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int ProcessedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
