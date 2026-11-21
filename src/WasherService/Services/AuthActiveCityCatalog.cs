using System.Net.Http.Json;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace GreenCarWash.WasherService.Services;

public interface IActiveCityCatalog
{
    Task<bool> IsActiveAsync(int cityId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, string>> NamesAsync(CancellationToken cancellationToken);
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
        var cities = await LoadAsync(cancellationToken);
        return cities.Ids.Contains(cityId);
    }

    public async Task<IReadOnlyDictionary<int, string>> NamesAsync(CancellationToken cancellationToken)
    {
        var cities = await LoadAsync(cancellationToken);
        return cities.Names;
    }

    private async Task<CityCache> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var cities = await _http.GetFromJsonAsync<List<AuthCity>>("api/auth/cities", cancellationToken) ?? [];
                return new CityCache(
                    cities.Select(city => city.CityId).ToHashSet(),
                    cities.ToDictionary(city => city.CityId, city => city.CityName));
            }) ?? new CityCache([], new Dictionary<int, string>());
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

    private sealed record CityCache(HashSet<int> Ids, Dictionary<int, string> Names);

    private sealed class AuthCity
    {
        public int CityId { get; set; }

        public string CityName { get; set; } = string.Empty;
    }
}
