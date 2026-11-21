using FluentValidation;
using GreenCarWash.AuthService.DTOs;

namespace GreenCarWash.AuthService.Validators;

public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain a digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
}

public static class PhoneRules
{
    public const string UsPhonePattern = @"^(\+1[\s.-]?)?\(?[2-9]\d{2}\)?[\s.-]?[2-9]\d{2}[\s.-]?\d{4}$";

    public static IRuleBuilderOptions<T, string> UsPhone<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(20)
            .Matches(UsPhonePattern)
            .WithMessage("Phone number must be a valid US number.");
}

public sealed class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(dto => dto.FullName).NotEmpty().MaximumLength(100);
        RuleFor(dto => dto.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(dto => dto.Password).ValidPassword();
        RuleFor(dto => dto.PhoneNumber).UsPhone();
        RuleFor(dto => dto.Role)
            .Must(role => role is "Customer" or "Washer")
            .WithMessage("Role must be Customer or Washer. Admin cannot be self-registered.");
    }
}

public sealed class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(dto => dto.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(dto => dto.Password).ValidPassword();
    }
}

public sealed class CityWriteDtoValidator : AbstractValidator<CityWriteDto>
{
    public CityWriteDtoValidator()
    {
        RuleFor(dto => dto.CityName).NotEmpty().MaximumLength(100);
        RuleFor(dto => dto.State).NotEmpty().MaximumLength(100);
    }
}
