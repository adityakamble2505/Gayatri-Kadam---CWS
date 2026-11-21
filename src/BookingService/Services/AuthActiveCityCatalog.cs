using System.Net.Http.Json;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace GreenCarWash.BookingService.Services;

public interface IActiveCityCatalog
{
    Task<bool> IsActiveAsync(int cityId, CancellationToken cancellationToken);
}

public sealed class AuthActiveCityCatalog : IActiveCityCatalog
{
    public const string CacheKey = "auth-active-cities";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthActiveCityCatalog> _logger;

    public AuthActiveCityCatalog(HttpClient http, IMemoryCache cache, ILogger<AuthActiveCityCatalog> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsActiveAsync(int cityId, CancellationToken cancellationToken)
    {
        try
        {
            var ids = await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var cities = await _http.GetFromJsonAsync<List<AuthCity>>("api/auth/cities", cancellationToken) ?? [];
                return (IReadOnlyList<int>)cities.Select(city => city.CityId).ToArray();
            }) ?? [];
            return ids.Contains(cityId);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutRejectedException
            or BrokenCircuitException)
        {
            _logger.LogWarning(exception, "Active city lookup failed");
            throw new FluentValidation.ValidationException(new[]
            {
                new ValidationFailure("CityId", "City could not be verified.")
            });
        }
    }

    private sealed class AuthCity
    {
        public int CityId { get; set; }
    }
}
