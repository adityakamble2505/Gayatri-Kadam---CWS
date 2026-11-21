using FluentValidation;
using GreenCarWash.BuildingBlocks;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.PaymentService.DTOs;
using GreenCarWash.PaymentService.Entities;
using GreenCarWash.PaymentService.Repositories;
using Microsoft.Extensions.Options;

namespace GreenCarWash.PaymentService.Services;

public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessAsync(Caller caller, ProcessPaymentDto dto, CancellationToken cancellationToken);

    Task<PaymentDto> GetForBookingAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task<PaymentDto> GetInternalByBookingAsync(int bookingId, CancellationToken cancellationToken);

    Task<PaymentReceiptDto> ReceiptAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task<RefundDto> RefundAsync(Caller caller, RefundRequestDto dto, CancellationToken cancellationToken);

    Task<RefundNoticeDto> RefundForCancelledBookingAsync(int bookingId, CancellationToken cancellationToken);
}

public sealed class PaymentProcessor : IPaymentProcessor
{
    private readonly IPaymentRepository _payments;
    private readonly IBookingDirectory _bookings;
    private readonly ICustomerDirectory _customers;
    private readonly IWasherDirectory _washers;
    private readonly IPaymentGateway _gateway;
    private readonly IValidator<ProcessPaymentDto> _processValidator;
    private readonly IValidator<RefundRequestDto> _refundValidator;
    private readonly PaymentOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<PaymentProcessor> _logger;

    public PaymentProcessor(
        IPaymentRepository payments,
        IBookingDirectory bookings,
        ICustomerDirectory customers,
        IWasherDirectory washers,
        IPaymentGateway gateway,
        IValidator<ProcessPaymentDto> processValidator,
        IValidator<RefundRequestDto> refundValidator,
        IOptions<PaymentOptions> options,
        IClock clock,
        ILogger<PaymentProcessor> logger)
    {
        _payments = payments;
        _bookings = bookings;
        _customers = customers;
        _washers = washers;
        _gateway = gateway;
        _processValidator = processValidator;
        _refundValidator = refundValidator;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessAsync(Caller caller, ProcessPaymentDto dto, CancellationToken cancellationToken)
    {
        await _processValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
        var booking = await _bookings.GetAsync(dto.BookingId, cancellationToken);
        if (booking.CustomerId != customer.CustomerId)
            throw new ForbiddenException("You can only pay for your own booking.");
        if (!_options.PayableStatuses.Contains(booking.Status, StringComparer.OrdinalIgnoreCase))
            throw new ConflictException("This booking cannot be paid.");
        if (booking.TotalAmount <= 0)
            throw Input.Invalid("Amount", "The booking total is not a payable amount.");

        var digits = CardNumbers.Digits(dto.CardNumber);
        var claim = await _payments.ClaimAsync(
            booking.BookingId,
            customer.CustomerId,
            booking.TotalAmount,
            CardNumbers.Brand(digits),
            digits[^4..],
            dto.IdempotencyKey.Trim(),
            _clock.UtcNow,
            cancellationToken);
        if (!claim.ShouldCharge)
            return new PaymentResult { Payment = ToDto(claim.Payment), Replay = true };

        var result = await _gateway.ChargeAsync(claim.Payment.CardLast4!, cancellationToken);
        if (result.TimedOut)
        {
            await FinishAsync(claim.Payment, booking, PaymentStatus.Failed, null, "Gateway timed out", "PaymentFailed", cancellationToken);
            throw new GatewayTimeoutException("The payment gateway timed out.");
        }

        if (!result.Approved)
        {
            var failed = await FinishAsync(claim.Payment, booking, PaymentStatus.Failed, null, result.Reason ?? "Card declined", "PaymentFailed", cancellationToken);
            return new PaymentResult { Payment = failed };
        }

        var paid = await FinishAsync(claim.Payment, booking, PaymentStatus.Success, result.TransactionReference, null, "PaymentSucceeded", cancellationToken);
        _logger.LogInformation("Payment {PaymentId} succeeded for booking {BookingId}", paid.PaymentId, booking.BookingId);
        return new PaymentResult { Payment = paid };
    }

    public async Task<PaymentDto> GetInternalByBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FindByBookingAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Payment", bookingId);
        return ToDto(payment);
    }

    public async Task<PaymentDto> GetForBookingAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FindByBookingAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Payment", bookingId);
        await AuthorizeReadAsync(caller, bookingId, cancellationToken);
        return ToDto(payment);
    }

    public async Task<PaymentReceiptDto> ReceiptAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FindByBookingAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Payment", bookingId);
        var booking = await AuthorizeReadAsync(caller, bookingId, cancellationToken);
        if (payment.PaymentStatus != PaymentStatus.Success || !booking.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("A receipt is available only after the wash is completed and the payment has succeeded.");

        return new PaymentReceiptDto
        {
            PaymentId = payment.PaymentId,
            TransactionReference = payment.TransactionReference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CardBrand = payment.CardBrand,
            CardLast4 = payment.CardLast4,
            PaymentDate = payment.PaymentDate,
            BookingId = booking.BookingId,
            BookingNumber = booking.BookingNumber,
            Package = booking.ServiceName,
            AddOns = booking.AddOns.Select(addOn => new ReceiptAddOnDto { Name = addOn.Name, Price = addOn.Price }).ToArray(),
            Total = booking.TotalAmount,
            AfterWashPhotoUrl = booking.AfterWashPhotoUrl
        };
    }

    public async Task<RefundDto> RefundAsync(Caller caller, RefundRequestDto dto, CancellationToken cancellationToken)
    {
        await _refundValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var payment = await ResolveAsync(dto, cancellationToken);
        var booking = await _bookings.GetAsync(payment.BookingId, cancellationToken);
        var amount = dto.Amount ?? payment.Amount - payment.RefundedAmount;
        if (amount <= 0)
            throw Input.Invalid("Amount", "There is no balance left to refund.");

        var decision = await _payments.RefundAsync(
            payment.PaymentId,
            amount,
            dto.Reason.Trim(),
            caller.UserId,
            booking.BookingId,
            booking.BookingNumber,
            booking.CustomerUserId,
            booking.WasherUserId,
            _clock.UtcNow,
            cancellationToken);
        return await ToRefundAsync(decision, payment.PaymentId, amount, dto.Reason.Trim(), cancellationToken);
    }

    public async Task<RefundNoticeDto> RefundForCancelledBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FindByBookingAsync(bookingId, cancellationToken);
        if (payment is null
            || payment.PaymentStatus is not (PaymentStatus.Success or PaymentStatus.PartiallyRefunded)
            || payment.RefundedAmount >= payment.Amount)
        {
            return new RefundNoticeDto { Message = "nothing to refund", Amount = 0 };
        }

        var booking = await _bookings.GetAsync(bookingId, cancellationToken);
        if (!booking.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Only a cancelled booking can be refunded by this endpoint.");

        var amount = payment.Amount - payment.RefundedAmount;
        var decision = await _payments.RefundAsync(
            payment.PaymentId,
            amount,
            "Booking cancelled",
            SystemActor.SystemUserId,
            booking.BookingId,
            booking.BookingNumber,
            booking.CustomerUserId,
            booking.WasherUserId,
            _clock.UtcNow,
            cancellationToken);
        if (decision.Decision != RefundDecision.Applied)
            return new RefundNoticeDto { Message = "nothing to refund", Amount = 0 };

        return new RefundNoticeDto { Message = "refunded", Amount = amount };
    }

    private async Task<PaymentDto> FinishAsync(
        Entities.Payment payment,
        BookingPaymentSnapshot booking,
        PaymentStatus status,
        string? reference,
        string? reason,
        string eventType,
        CancellationToken cancellationToken)
    {
        await _payments.FinishAsync(
            payment,
            status,
            reference,
            reason,
            eventType,
            booking.BookingId,
            booking.BookingNumber,
            booking.CustomerUserId,
            booking.WasherUserId,
            _clock.UtcNow,
            cancellationToken);
        return ToDto(payment);
    }

    private async Task<BookingPaymentSnapshot> AuthorizeReadAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetAsync(bookingId, cancellationToken);
        if (caller.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
            if (customer.CustomerId == booking.CustomerId)
                return booking;
        }
        else if (caller.Role.Equals("Washer", StringComparison.OrdinalIgnoreCase) && booking.WasherId is int washerId)
        {
            var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
            if (washer.WasherId == washerId)
                return booking;
        }

        throw new ForbiddenException("You cannot view this payment.");
    }

    private async Task<Entities.Payment> ResolveAsync(RefundRequestDto dto, CancellationToken cancellationToken)
    {
        if (dto.PaymentId is int paymentId)
        {
            return await _payments.FindAsync(paymentId, cancellationToken)
                ?? throw new NotFoundException("Payment", paymentId);
        }

        return await _payments.FindByBookingAsync(dto.BookingId!.Value, cancellationToken)
            ?? throw new NotFoundException("Payment", dto.BookingId.Value);
    }

    private async Task<RefundDto> ToRefundAsync(
        RefundResult decision,
        int paymentId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        switch (decision.Decision)
        {
            case RefundDecision.ExceedsBalance:
                throw Input.Invalid("Amount", "Refund exceeds the remaining balance.");
            case RefundDecision.NotRefundable:
                throw new ConflictException("This payment cannot be refunded.");
            case RefundDecision.NotFound:
                throw new NotFoundException("Payment", paymentId);
        }

        var payment = await _payments.FindAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);
        return new RefundDto
        {
            RefundId = decision.RefundId,
            PaymentId = payment.PaymentId,
            Amount = amount,
            Reason = reason,
            Status = payment.PaymentStatus.ToString(),
            RefundedAmount = payment.RefundedAmount
        };
    }

    private static PaymentDto ToDto(Entities.Payment payment) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        CustomerId = payment.CustomerId,
        Amount = payment.Amount,
        Currency = payment.Currency,
        PaymentMethod = payment.PaymentMethod.ToString(),
        CardBrand = payment.CardBrand,
        CardLast4 = payment.CardLast4,
        Status = payment.PaymentStatus.ToString(),
        TransactionReference = payment.TransactionReference,
        IdempotencyKey = payment.IdempotencyKey,
        FailureReason = payment.FailureReason,
        RefundedAmount = payment.RefundedAmount,
        PaymentDate = payment.PaymentDate
    };
}
