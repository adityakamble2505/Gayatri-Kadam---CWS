using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GreenCarWash.AuthService.Entities;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.IdentityModel.Tokens;

namespace GreenCarWash.AuthService.Services;

public sealed record IssuedRefreshToken(string RawToken, RefreshToken Entity);

public interface ITokenService
{
    (string AccessToken, DateTime ExpiresAt) CreateAccessToken(User user);

    IssuedRefreshToken CreateRefreshToken(User user);
}

public static class TokenHasher
{
    public static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class TokenService : ITokenService
{
    public const string UserIdClaim = "userId";
    public const string EmailClaim = "email";
    public const string RoleClaim = "role";

    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _key;
    private readonly int _accessTokenMinutes;
    private readonly int _refreshTokenDays;
    private readonly IClock _clock;

    public TokenService(IConfiguration configuration, IClock clock)
    {
        _issuer = Required(configuration, "Jwt:Issuer");
        _audience = Required(configuration, "Jwt:Audience");
        _key = Required(configuration, "Jwt:Key");
        _accessTokenMinutes = configuration.GetValue("Jwt:AccessTokenMinutes", 15);
        _refreshTokenDays = configuration.GetValue("Jwt:RefreshTokenDays", 7);
        _clock = clock;
    }

    public (string AccessToken, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var expiresAt = _clock.UtcNow.AddMinutes(_accessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(UserIdClaim, user.UserId.ToString()),
            new Claim(EmailClaim, user.Email),
            new Claim(RoleClaim, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
                SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public IssuedRefreshToken CreateRefreshToken(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = user.UserId,
            TokenHash = TokenHasher.Sha256(rawToken),
            ExpiresAt = _clock.UtcNow.AddDays(_refreshTokenDays),
            CreatedAt = _clock.UtcNow
        };

        return new IssuedRefreshToken(rawToken, entity);
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is not configured.");

        return value;
    }
}
