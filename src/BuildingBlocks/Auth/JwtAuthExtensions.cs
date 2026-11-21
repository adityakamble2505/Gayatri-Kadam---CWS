using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GreenCarWash.BuildingBlocks.Auth;

public static class JwtAuthExtensions
{
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = Required(configuration, "Jwt:Issuer");
        var audience = Required(configuration, "Jwt:Audience");
        var key = Required(configuration, "Jwt:Key");

        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = "role",
                    NameClaimType = "sub"
                };
            });

        return services;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is not configured.");

        return value;
    }
}
