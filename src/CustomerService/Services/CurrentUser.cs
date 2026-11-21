using System.Globalization;
using System.Security.Claims;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.CustomerService.Services;

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
}
