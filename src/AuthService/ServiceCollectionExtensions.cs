using GreenCarWash.AuthService.Auth;
using GreenCarWash.AuthService.Repositories;
using GreenCarWash.AuthService.Services;
using Microsoft.AspNetCore.Authorization;

namespace GreenCarWash.AuthService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthenticationService>();
        services.AddScoped<ICityService, CityService>();
        services.AddSingleton<IAuthorizationHandler, AdminOrServiceHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminOrServiceHandler.PolicyName, policy =>
                policy.Requirements.Add(new AdminOrServiceRequirement()));
        });
        return services;
    }
}
