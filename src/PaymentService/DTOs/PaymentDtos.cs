namespace GreenCarWash.PaymentService.DTOs;

public sealed class ProcessPaymentDto
{
    public int BookingId { get; set; }

    public string CardNumber { get; set; } = string.Empty;

    public int ExpiryMonth { get; set; }

    public int ExpiryYear { get; set; }

    public string Cvv { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class PaymentDto
{
    public int PaymentId { get; init; }

    public int BookingId { get; init; }

    public int CustomerId { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    public string PaymentMethod { get; init; } = string.Empty;

    public string? CardBrand { get; init; }

    public string? CardLast4 { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? TransactionReference { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string? FailureReason { get; init; }

    public decimal RefundedAmount { get; init; }

    public DateTime? PaymentDate { get; init; }
}

public sealed class PaymentResult
{
    public PaymentDto Payment { get; init; } = new();

    public bool Replay { get; init; }
}

public sealed class RefundRequestDto
{
    public int? PaymentId { get; set; }

    public int? BookingId { get; set; }

    public decimal? Amount { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class RefundDto
{
    public int RefundId { get; init; }

    public int PaymentId { get; init; }

    public decimal Amount { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal RefundedAmount { get; init; }
}

public sealed class RefundNoticeDto
{
    public string Message { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

public sealed class ReceiptAddOnDto
{
    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}

public sealed class PaymentReceiptDto
{
    public int PaymentId { get; init; }

    public string? TransactionReference { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    public string? CardBrand { get; init; }

    public string? CardLast4 { get; init; }

    public DateTime? PaymentDate { get; init; }

    public int BookingId { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public string Package { get; init; } = string.Empty;

    public IReadOnlyList<ReceiptAddOnDto> AddOns { get; init; } = [];

    public decimal Total { get; init; }

    public string? AfterWashPhotoUrl { get; init; }
}

public sealed class BookingRefundRequest
{
    public int BookingId { get; set; }
}
