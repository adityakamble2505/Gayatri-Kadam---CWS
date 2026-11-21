using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GreenCarWash.BuildingBlocks.Http;
using Microsoft.AspNetCore.Authorization;

namespace GreenCarWash.AuthService.Auth;

public sealed class AdminOrServiceRequirement : IAuthorizationRequirement;

public sealed class AdminOrServiceHandler : AuthorizationHandler<AdminOrServiceRequirement>
{
    public const string PolicyName = "AdminOrService";

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminOrServiceHandler(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminOrServiceRequirement requirement)
    {
        if (HasRole(context.User, "Admin") || HasRole(context.User, "Service") || HasValidInternalKey())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        user.IsInRole(role)
        || user.Claims.Any(claim =>
            claim.Value == role && (claim.Type == "role" || claim.Type == ClaimTypes.Role));

    private bool HasValidInternalKey()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return false;

        if (!httpContext.Request.Headers.TryGetValue(GreenCarWashHeaders.InternalKey, out var providedValues))
            return false;

        var configured = _configuration["InternalApi:Key"];
        if (string.IsNullOrEmpty(configured))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(providedValues.ToString());
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return providedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
