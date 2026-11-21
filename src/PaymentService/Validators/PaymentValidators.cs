using FluentValidation;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.PaymentService.DTOs;
using GreenCarWash.PaymentService.Services;

namespace GreenCarWash.PaymentService.Validators;

public sealed class ProcessPaymentDtoValidator : AbstractValidator<ProcessPaymentDto>
{
    public ProcessPaymentDtoValidator(IClock clock)
    {
        RuleFor(dto => dto.BookingId).GreaterThan(0);
        RuleFor(dto => dto.CardNumber)
            .Must(CardNumbers.IsValid)
            .WithMessage("Card number is invalid.");
        RuleFor(dto => dto.ExpiryMonth).InclusiveBetween(1, 12);
        RuleFor(dto => dto.ExpiryYear).GreaterThan(2000);
        RuleFor(dto => dto)
            .Must(dto => !CardNumbers.Expired(dto.ExpiryMonth, dto.ExpiryYear, clock.UtcNow))
            .WithMessage("Card expiry is in the past.");
        RuleFor(dto => dto.Cvv)
            .Matches("^\\d{3,4}$")
            .WithMessage("CVV must be 3 or 4 digits.");
        RuleFor(dto => dto.IdempotencyKey).NotEmpty().MaximumLength(64);
    }
}

public sealed class RefundRequestDtoValidator : AbstractValidator<RefundRequestDto>
{
    public RefundRequestDtoValidator()
    {
        RuleFor(dto => dto.Reason).NotEmpty().MaximumLength(300);
        RuleFor(dto => dto)
            .Must(dto => dto.PaymentId is > 0 || dto.BookingId is > 0)
            .WithMessage("PaymentId or BookingId is required.");
        RuleFor(dto => dto.Amount)
            .GreaterThan(0)
            .When(dto => dto.Amount.HasValue)
            .WithMessage("Refund amount must be greater than zero.");
    }
}
