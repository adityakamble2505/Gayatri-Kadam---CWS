using System.Net.Http.Json;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.BookingService.Services;

public interface IUserDirectory
{
    Task<string> FullNameAsync(int userId, CancellationToken cancellationToken);
}

public sealed class AuthUserDirectory : IUserDirectory
{
    private readonly HttpClient _http;

    public AuthUserDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> FullNameAsync(int userId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/auth/user/{userId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Auth");
        var user = await response.Content.ReadFromJsonAsync<AuthUserName>(cancellationToken)
            ?? throw new BadGatewayException("Auth service returned an empty user.");
        return user.FullName;
    }

    private sealed class AuthUserName
    {
        public string FullName { get; set; } = string.Empty;
    }
}
