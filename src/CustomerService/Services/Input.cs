using FluentValidation;
using FluentValidation.Results;

namespace GreenCarWash.CustomerService.Services;

internal static class Input
{
    public static ValidationException Invalid(string property, string message) =>
        new(new[] { new ValidationFailure(property, message) });
}
