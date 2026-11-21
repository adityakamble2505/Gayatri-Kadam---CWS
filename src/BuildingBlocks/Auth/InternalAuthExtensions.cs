using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GreenCarWash.BuildingBlocks.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreenCarWash.BuildingBlocks.Auth;

public sealed class ServiceOnlyRequirement : IAuthorizationRequirement;

public sealed class ServiceOnlyAuthorizationHandler : AuthorizationHandler<ServiceOnlyRequirement>
{
    public const string PolicyName = "ServiceOnly";

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ServiceOnlyAuthorizationHandler(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceOnlyRequirement requirement)
    {
        if (HasServiceRole(context.User) || HasValidInternalKey())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static bool HasServiceRole(ClaimsPrincipal user)
    {
        return user.IsInRole("Service")
            || user.Claims.Any(claim =>
                claim.Value == "Service"
                && (claim.Type == "role" || claim.Type == ClaimTypes.Role));
    }

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

        var provided = providedValues.ToString();
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return providedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}

public static class InternalAuthExtensions
{
    public static IServiceCollection AddInternalAuth(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["InternalApi:Key"]))
            throw new InvalidOperationException("InternalApi:Key is not configured.");

        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, ServiceOnlyAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ServiceOnlyAuthorizationHandler.PolicyName, policy =>
                policy.Requirements.Add(new ServiceOnlyRequirement()));
        });

        return services;
    }
}
