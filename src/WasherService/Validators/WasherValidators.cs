using FluentValidation;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Entities;

namespace GreenCarWash.WasherService.Validators;

public sealed class CreateWasherProfileDtoValidator : AbstractValidator<CreateWasherProfileDto>
{
    public CreateWasherProfileDtoValidator()
    {
        RuleFor(dto => dto.CityId).GreaterThan(0);
    }
}

public sealed class AvailabilityDtoValidator : AbstractValidator<AvailabilityDto>
{
    public AvailabilityDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Latitude.HasValue == dto.Longitude.HasValue)
            .WithMessage("Latitude and longitude must both be provided.");
        RuleFor(dto => dto.Latitude).InclusiveBetween(-90, 90).When(dto => dto.Latitude.HasValue);
        RuleFor(dto => dto.Longitude).InclusiveBetween(-180, 180).When(dto => dto.Longitude.HasValue);
    }
}

public sealed class LocationDtoValidator : AbstractValidator<LocationDto>
{
    public LocationDtoValidator()
    {
        RuleFor(dto => dto.Latitude).InclusiveBetween(-90, 90);
        RuleFor(dto => dto.Longitude).InclusiveBetween(-180, 180);
    }
}

public sealed class AdminWasherWriteDtoValidator : AbstractValidator<AdminWasherWriteDto>
{
    public AdminWasherWriteDtoValidator()
    {
        RuleFor(dto => dto.UserId).GreaterThan(0);
        RuleFor(dto => dto.CityId).GreaterThan(0);
    }
}

public sealed class AdminWasherUpdateDtoValidator : AbstractValidator<AdminWasherUpdateDto>
{
    public AdminWasherUpdateDtoValidator()
    {
        RuleFor(dto => dto.CityId).GreaterThan(0);
        RuleFor(dto => dto)
            .Must(dto => dto.Latitude.HasValue == dto.Longitude.HasValue)
            .WithMessage("Latitude and longitude must both be provided.");
        RuleFor(dto => dto.Latitude).InclusiveBetween(-90, 90).When(dto => dto.Latitude.HasValue);
        RuleFor(dto => dto.Longitude).InclusiveBetween(-180, 180).When(dto => dto.Longitude.HasValue);
    }
}

public sealed class NextWasherDtoValidator : AbstractValidator<NextWasherDto>
{
    public NextWasherDtoValidator()
    {
        RuleFor(dto => dto.CityId).GreaterThan(0);
        RuleFor(dto => dto.ExcludeIds).NotNull();
        RuleForEach(dto => dto.ExcludeIds).GreaterThan(0);
        RuleFor(dto => dto.Strategy)
            .Must(strategy => string.IsNullOrWhiteSpace(strategy) || strategy.Equals("Fifo", StringComparison.OrdinalIgnoreCase)
                || strategy.Equals("TopRated", StringComparison.OrdinalIgnoreCase)
                || strategy.Equals("Nearest", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Strategy must be Fifo, TopRated, or Nearest.");
        RuleFor(dto => dto.Latitude).InclusiveBetween(-90, 90).When(dto => dto.Latitude.HasValue);
        RuleFor(dto => dto.Longitude).InclusiveBetween(-180, 180).When(dto => dto.Longitude.HasValue);
        RuleFor(dto => dto)
            .Must(dto => dto.Latitude.HasValue && dto.Longitude.HasValue)
            .When(dto => dto.Strategy is not null && dto.Strategy.Equals("Nearest", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Latitude and longitude are required for the Nearest strategy.");
    }
}

public sealed class ReleaseWasherDtoValidator : AbstractValidator<ReleaseWasherDto>
{
    public ReleaseWasherDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
    }
}

public sealed class ConfirmAssignmentDtoValidator : AbstractValidator<ConfirmAssignmentDto>
{
    public ConfirmAssignmentDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
        RuleFor(dto => dto.BookingId).GreaterThan(0);
    }
}

public sealed class WasherIdDtoValidator : AbstractValidator<WasherIdDto>
{
    public WasherIdDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
    }
}

public sealed class AssignmentUpdateDtoValidator : AbstractValidator<AssignmentUpdateDto>
{
    public AssignmentUpdateDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
        RuleFor(dto => dto.AssignmentStatus)
            .Must(status => Enum.TryParse<AssignmentStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage("Assignment status must be Available, Assigned, Accepted, Rejected, or InProgress.");
    }
}

public sealed class RatingUpdateDtoValidator : AbstractValidator<RatingUpdateDto>
{
    public RatingUpdateDtoValidator()
    {
        RuleFor(dto => dto.WasherId).GreaterThan(0);
        RuleFor(dto => dto.Rating).InclusiveBetween(1, 5);
    }
}
