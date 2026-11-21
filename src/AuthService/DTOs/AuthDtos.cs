namespace GreenCarWash.AuthService.DTOs;

public sealed class RegisterDto
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public sealed class LoginDto
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class UserResponseDto
{
    public int UserId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}

public sealed class AuthResponseDto
{
    public string AccessToken { get; init; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; init; }

    public string RefreshToken { get; init; } = string.Empty;

    public UserResponseDto User { get; init; } = new();
}

public sealed class CityResponseDto
{
    public int CityId { get; init; }

    public string CityName { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}

public sealed class CityWriteDto
{
    public string CityName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;
}
