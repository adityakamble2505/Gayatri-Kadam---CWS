using FluentValidation;
using GreenCarWash.CatalogService.DTOs;

namespace GreenCarWash.CatalogService.Validators;

public sealed class ServiceWriteDtoValidator : AbstractValidator<ServiceWriteDto>
{
    public ServiceWriteDtoValidator()
    {
        RuleFor(dto => dto.ServiceName).NotEmpty().MaximumLength(100);
        RuleFor(dto => dto.Description).MaximumLength(500);
        RuleFor(dto => dto.Price).GreaterThanOrEqualTo(0);
        RuleFor(dto => dto.Discount).InclusiveBetween(0, 100);
        RuleFor(dto => dto.Duration).GreaterThan(0);
        RuleFor(dto => dto.WaterSavedLiters).GreaterThanOrEqualTo(0);
    }
}

public sealed class AddOnWriteDtoValidator : AbstractValidator<AddOnWriteDto>
{
    public AddOnWriteDtoValidator()
    {
        RuleFor(dto => dto.AddOnName).NotEmpty().MaximumLength(100);
        RuleFor(dto => dto.Price).GreaterThanOrEqualTo(0);
    }
}

public sealed class PromoWriteDtoValidator : AbstractValidator<PromoWriteDto>
{
    public PromoWriteDtoValidator()
    {
        RuleFor(dto => dto.Code).NotEmpty().MaximumLength(30);
        RuleFor(dto => dto.DiscountType)
            .Must(type => type.Equals("Percent", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Flat", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Discount type must be Percent or Flat.");
        RuleFor(dto => dto.DiscountValue).GreaterThan(0);
        RuleFor(dto => dto.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(dto => dto.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Percent discount cannot exceed 100.");
        RuleFor(dto => dto.MinOrderAmount).GreaterThanOrEqualTo(0);
        RuleFor(dto => dto.MaxDiscount).GreaterThanOrEqualTo(0).When(dto => dto.MaxDiscount.HasValue);
        RuleFor(dto => dto.ValidTo).GreaterThan(dto => dto.ValidFrom);
        RuleFor(dto => dto.UsageLimit).GreaterThan(0).When(dto => dto.UsageLimit.HasValue);
    }
}

public sealed class PromoCheckDtoValidator : AbstractValidator<PromoCheckDto>
{
    public PromoCheckDtoValidator()
    {
        RuleFor(dto => dto.Code).NotEmpty().MaximumLength(30);
        RuleFor(dto => dto.Subtotal).GreaterThanOrEqualTo(0);
    }
}

public sealed class PromoReleaseDtoValidator : AbstractValidator<PromoReleaseDto>
{
    public PromoReleaseDtoValidator()
    {
        RuleFor(dto => dto.Code).NotEmpty().MaximumLength(30);
    }
}

public sealed class QuoteRequestDtoValidator : AbstractValidator<QuoteRequestDto>
{
    public QuoteRequestDtoValidator()
    {
        RuleFor(dto => dto.ServiceId).GreaterThan(0);
        RuleFor(dto => dto.AddOnIds).NotNull();
        RuleForEach(dto => dto.AddOnIds).GreaterThan(0);
        RuleFor(dto => dto.PromoCode).MaximumLength(30);
    }
}
