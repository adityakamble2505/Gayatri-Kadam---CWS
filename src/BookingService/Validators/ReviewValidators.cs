using FluentValidation;
using GreenCarWash.BookingService.DTOs;

namespace GreenCarWash.BookingService.Validators;

public sealed class CreateReviewDtoValidator : AbstractValidator<CreateReviewDto>
{
    public CreateReviewDtoValidator()
    {
        RuleFor(dto => dto.BookingId).GreaterThan(0);
        RuleFor(dto => dto.Rating).InclusiveBetween(1, 5);
        RuleFor(dto => dto.Comments).MaximumLength(1000);
    }
}
