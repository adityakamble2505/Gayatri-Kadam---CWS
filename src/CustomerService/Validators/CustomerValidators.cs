using FluentValidation;
using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Entities;

namespace GreenCarWash.CustomerService.Validators;

public sealed class ProfileWriteDtoValidator : AbstractValidator<ProfileWriteDto>
{
    public ProfileWriteDtoValidator()
    {
        RuleFor(dto => dto.CityId).GreaterThan(0);
        RuleFor(dto => dto.Address).NotEmpty().MaximumLength(500);
        RuleFor(dto => dto)
            .Must(dto => dto.Latitude.HasValue == dto.Longitude.HasValue)
            .WithMessage("Latitude and longitude must both be provided.");
        RuleFor(dto => dto.Latitude).InclusiveBetween(-90, 90).When(dto => dto.Latitude.HasValue);
        RuleFor(dto => dto.Longitude).InclusiveBetween(-180, 180).When(dto => dto.Longitude.HasValue);
    }
}

public sealed class VehicleWriteDtoValidator : AbstractValidator<VehicleWriteDto>
{
    public VehicleWriteDtoValidator()
    {
        RuleFor(dto => dto.VehicleNumber).NotEmpty().MaximumLength(20);
        RuleFor(dto => dto.Brand).NotEmpty().MaximumLength(60);
        RuleFor(dto => dto.Model).NotEmpty().MaximumLength(60);
        RuleFor(dto => dto.VehicleType)
            .Must(type => Enum.TryParse<VehicleType>(type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage("Vehicle type must be Sedan, SUV, or Hatchback.");
    }
}
