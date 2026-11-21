using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.BookingService.Services;

internal static class Input
{
    public static ValidationException Invalid(string property, string message) =>
        new(new[] { new ValidationFailure(property, message) });
}

public sealed record Caller(int UserId, string Role);

public static class CurrentUser
{
    public const string UserIdClaim = "userId";

    public static int Id(ClaimsPrincipal user)
    {
        var raw = user.FindFirst(UserIdClaim)?.Value;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            throw new UnauthorizedException("Authentication is required.");
        return userId;
    }

    public static Caller From(ClaimsPrincipal user)
    {
        var role = user.FindFirst("role")?.Value
            ?? user.FindFirst(ClaimTypes.Role)?.Value
            ?? string.Empty;
        return new Caller(Id(user), role);
    }
}
