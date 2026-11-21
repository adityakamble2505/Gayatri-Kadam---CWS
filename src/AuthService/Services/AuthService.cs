using FluentValidation;
using GreenCarWash.AuthService.DTOs;
using GreenCarWash.AuthService.Entities;
using GreenCarWash.AuthService.Repositories;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Services;

public interface IAuthService
{
    Task<UserResponseDto> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken);

    Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken cancellationToken);

    Task<AuthResponseDto> RefreshAsync(RefreshRequestDto dto, CancellationToken cancellationToken);

    Task LogoutAsync(LogoutRequestDto dto, CancellationToken cancellationToken);

    Task<UserResponseDto> GetUserAsync(int userId, CancellationToken cancellationToken);
}

public sealed class AuthenticationService : IAuthService
{
    public const string InvalidCredentialsMessage = "Invalid email or password.";
    public const string InvalidRefreshTokenMessage = "Invalid refresh token.";
    public const int LockoutThreshold = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwords;
    private readonly ITokenService _tokens;
    private readonly IValidator<RegisterDto> _registerValidator;
    private readonly IValidator<LoginDto> _loginValidator;
    private readonly IClock _clock;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwords,
        ITokenService tokens,
        IValidator<RegisterDto> registerValidator,
        IValidator<LoginDto> loginValidator,
        IClock clock,
        ILogger<AuthenticationService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwords = passwords;
        _tokens = tokens;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<UserResponseDto> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken)
    {
        await _registerValidator.ValidateAndThrowAsync(dto, cancellationToken);

        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: false, out var role) || role is UserRole.Admin)
            throw new ValidationException("Role must be Customer or Washer. Admin cannot be self-registered.");

        var email = dto.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, cancellationToken) is not null)
            throw new ConflictException("An account with this email already exists.");

        var now = _clock.UtcNow;
        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PasswordHash = _passwords.Hash(dto.Password),
            PhoneNumber = dto.PhoneNumber.Trim(),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _users.AddAsync(user, cancellationToken);
        try
        {
            await SqlServerErrors.ExecuteOnceAsync(() => _users.SaveChangesAsync(cancellationToken));
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsDuplicateKey(exception))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        _logger.LogInformation("User registered. UserId: {UserId} Role: {Role}", user.UserId, user.Role);
        return ToDto(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken cancellationToken)
    {
        await _loginValidator.ValidateAndThrowAsync(dto, cancellationToken);

        var user = await _users.FindByEmailAsync(dto.Email, cancellationToken);
        if (user is null || !user.IsActive)
            throw new UnauthorizedException(InvalidCredentialsMessage);

        if (user.LockoutEndUtc is { } lockedUntil && lockedUntil > _clock.UtcNow)
            throw new UnauthorizedException(InvalidCredentialsMessage);

        if (!_passwords.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= LockoutThreshold)
            {
                user.LockoutEndUtc = _clock.UtcNow.Add(LockoutDuration);
                _logger.LogWarning("Account locked out. UserId: {UserId}", user.UserId);
            }
            else
            {
                _logger.LogWarning("Login failed. UserId: {UserId}", user.UserId);
            }

            user.UpdatedAt = _clock.UtcNow;
            await SqlServerErrors.ExecuteOnceAsync(() => _users.SaveChangesAsync(cancellationToken));
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = _clock.UtcNow;
        user.UpdatedAt = _clock.UtcNow;

        var response = await IssueTokensAsync(user, cancellationToken);
        _logger.LogInformation("User logged in. UserId: {UserId}", user.UserId);
        return response;
    }

    public async Task<AuthResponseDto> RefreshAsync(RefreshRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            throw new UnauthorizedException(InvalidRefreshTokenMessage);

        var stored = await _refreshTokens.FindByHashAsync(TokenHasher.Sha256(dto.RefreshToken), cancellationToken);
        if (stored?.User is null || stored.RevokedAt is not null || stored.ExpiresAt <= _clock.UtcNow || !stored.User.IsActive)
            throw new UnauthorizedException(InvalidRefreshTokenMessage);

        var user = stored.User;
        stored.RevokedAt = _clock.UtcNow;
        var issued = _tokens.CreateRefreshToken(user);
        var (accessToken, expiresAt) = _tokens.CreateAccessToken(user);
        await _refreshTokens.AddAsync(issued.Entity, cancellationToken);
        await SqlServerErrors.ExecuteOnceAsync(async () =>
        {
            await using var transaction = await _users.BeginTransactionAsync(cancellationToken);
            await _users.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        });

        _logger.LogInformation("Refresh token rotated. UserId: {UserId}", user.UserId);
        return new AuthResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = expiresAt,
            RefreshToken = issued.RawToken,
            User = ToDto(user)
        };
    }

    public async Task LogoutAsync(LogoutRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return;

        var stored = await _refreshTokens.FindByHashAsync(TokenHasher.Sha256(dto.RefreshToken), cancellationToken);
        if (stored is null || stored.RevokedAt is not null)
            return;

        stored.RevokedAt = _clock.UtcNow;
        await SqlServerErrors.ExecuteOnceAsync(() => _users.SaveChangesAsync(cancellationToken));
        _logger.LogInformation("Refresh token revoked. UserId: {UserId}", stored.UserId);
    }

    public async Task<UserResponseDto> GetUserAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", userId);

        return ToDto(user);
    }

    private async Task<AuthResponseDto> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var issued = _tokens.CreateRefreshToken(user);
        var (accessToken, expiresAt) = _tokens.CreateAccessToken(user);
        await _refreshTokens.AddAsync(issued.Entity, cancellationToken);
        await SqlServerErrors.ExecuteOnceAsync(() => _users.SaveChangesAsync(cancellationToken));
        return new AuthResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = expiresAt,
            RefreshToken = issued.RawToken,
            User = ToDto(user)
        };
    }

    private static UserResponseDto ToDto(User user) => new()
    {
        UserId = user.UserId,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role.ToString(),
        IsActive = user.IsActive
    };

}
