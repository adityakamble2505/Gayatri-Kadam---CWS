using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.Extensions.Caching.Memory;

namespace GreenCarWash.BookingService.Services;

public static class PublicName
{
    public static string Format(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0];

        return $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}

public sealed class DisplayNameLookup
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly ICustomerDirectory _customers;
    private readonly IWasherDirectory _washers;
    private readonly IUserDirectory _users;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DisplayNameLookup> _logger;

    public DisplayNameLookup(
        ICustomerDirectory customers,
        IWasherDirectory washers,
        IUserDirectory users,
        IMemoryCache cache,
        ILogger<DisplayNameLookup> logger)
    {
        _customers = customers;
        _washers = washers;
        _users = users;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> CustomerAsync(int customerId, bool privacy, CancellationToken cancellationToken)
    {
        var userId = await CachedAsync(
            "customer-user:" + customerId,
            async () => (await _customers.GetByIdAsync(customerId, cancellationToken)).UserId,
            cancellationToken);
        var fullName = await CachedAsync(
            "user-name:" + userId,
            () => _users.FullNameAsync(userId, cancellationToken),
            cancellationToken);
        return privacy ? PublicName.Format(fullName) : fullName;
    }

    public async Task<string> WasherAsync(int washerId, CancellationToken cancellationToken)
    {
        try
        {
            var userId = await CachedAsync(
                "washer-user:" + washerId,
                async () => (await _washers.GetByIdAsync(washerId, cancellationToken)).UserId,
                cancellationToken);
            return await CachedAsync(
                "user-name:" + userId,
                () => _users.FullNameAsync(userId, cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Washer name lookup failed for {WasherId}", washerId);
            return $"Washer {washerId}";
        }
    }

    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        cancellationToken.ThrowIfCancellationRequested();
        var value = await load();
        _cache.Set(key, value, CacheFor);
        return value;
    }
}
