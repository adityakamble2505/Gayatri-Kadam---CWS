using FluentValidation;
using GreenCarWash.BookingService.DTOs;

namespace GreenCarWash.BookingService.Validators;

public sealed class CreateBookingDtoValidator : AbstractValidator<CreateBookingDto>
{
    public CreateBookingDtoValidator()
    {
        RuleFor(dto => dto.VehicleId).GreaterThan(0);
        RuleFor(dto => dto.ServiceId).GreaterThan(0);
        RuleFor(dto => dto.AddOnIds).NotNull();
        RuleForEach(dto => dto.AddOnIds).GreaterThan(0);
        RuleFor(dto => dto.PromoCode).MaximumLength(30);
        RuleFor(dto => dto.Instructions).MaximumLength(1000);
    }
}

public sealed class ScheduleBookingDtoValidator : AbstractValidator<ScheduleBookingDto>
{
    public ScheduleBookingDtoValidator()
    {
        RuleFor(dto => dto.VehicleId).GreaterThan(0);
        RuleFor(dto => dto.ServiceId).GreaterThan(0);
        RuleFor(dto => dto.AddOnIds).NotNull();
        RuleForEach(dto => dto.AddOnIds).GreaterThan(0);
        RuleFor(dto => dto.PromoCode).MaximumLength(30);
        RuleFor(dto => dto.Instructions).MaximumLength(1000);
        RuleFor(dto => dto.BookingDate).NotEqual(default(DateTime)).WithMessage("Booking date is required.");
    }
}

public sealed class CancelBookingDtoValidator : AbstractValidator<CancelBookingDto>
{
    public CancelBookingDtoValidator()
    {
        RuleFor(dto => dto.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class AssignWasherDtoValidator : AbstractValidator<AssignWasherDto>
{
    public AssignWasherDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
    }
}

public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleFor(dto => dto.Scope)
            .Must(scope => scope.Equals("current", StringComparison.OrdinalIgnoreCase)
                || scope.Equals("past", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Scope must be current or past.");
    }
}
