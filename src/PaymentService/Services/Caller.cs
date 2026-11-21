using System.Security.Claims;
using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.PaymentService.Services;

internal static class Input
{
    public static ValidationException Invalid(string property, string message) =>
        new(new[] { new ValidationFailure(property, message) });
}

public sealed record Caller(int UserId, string Role);

public static class CurrentUser
{
    public static Caller From(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("userId")?.Value;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            throw new UnauthorizedException("Authentication is required.");
        var role = user.FindFirst("role")?.Value
            ?? user.FindFirst(ClaimTypes.Role)?.Value
            ?? string.Empty;
        return new Caller(userId, role);
    }
}
