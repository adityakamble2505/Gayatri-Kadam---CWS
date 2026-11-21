using System.Net;
using System.Net.Http.Json;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.Extensions.Caching.Memory;

namespace GreenCarWash.NotificationService.Services;

public sealed record UserContact(int UserId, string FullName, string Email, bool IsActive);

public sealed record BookingNotice(
    string ServiceName,
    IReadOnlyList<BookingAddOnNotice> AddOns,
    decimal TotalAmount,
    string? AfterWashPhotoUrl,
    string? CancelledBy);

public sealed record BookingAddOnNotice(string Name, decimal Price);

public interface IUserDirectory
{
    Task<UserContact?> GetAsync(int userId, CancellationToken cancellationToken);
}

public interface IBookingNoticeDirectory
{
    Task<BookingNotice> GetAsync(int bookingId, CancellationToken cancellationToken);
}

public sealed class UserDirectory : IUserDirectory
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public UserDirectory(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<UserContact?> GetAsync(int userId, CancellationToken cancellationToken)
    {
        var key = "auth-user-" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_cache.TryGetValue(key, out UserContact? cached))
            return cached;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/auth/user/{userId}", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new ServiceUnavailableException("Auth service is unavailable.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new ServiceUnavailableException("Auth service is unavailable.");

        var body = await response.Content.ReadFromJsonAsync<AuthUserBody>(cancellationToken)
            ?? throw new ServiceUnavailableException("Auth service is unavailable.");
        var contact = new UserContact(body.UserId, body.FullName ?? string.Empty, body.Email ?? string.Empty, body.IsActive);
        _cache.Set(key, contact, TimeSpan.FromMinutes(10));
        return contact;
    }

    private sealed class AuthUserBody
    {
        public int UserId { get; set; }

        public string? FullName { get; set; }

        public string? Email { get; set; }

        public bool IsActive { get; set; }
    }
}

public sealed class BookingNoticeDirectory : IBookingNoticeDirectory
{
    private readonly HttpClient _http;

    public BookingNoticeDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<BookingNotice> GetAsync(int bookingId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/bookings/internal/{bookingId}", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new ServiceUnavailableException("Booking service is unavailable.");
        }

        if (!response.IsSuccessStatusCode)
            throw new ServiceUnavailableException("Booking service is unavailable.");

        var body = await response.Content.ReadFromJsonAsync<BookingBody>(cancellationToken)
            ?? throw new ServiceUnavailableException("Booking service is unavailable.");
        return new BookingNotice(
            body.ServiceName ?? string.Empty,
            (body.AddOns ?? []).Select(addOn => new BookingAddOnNotice(addOn.Name ?? string.Empty, addOn.Price)).ToArray(),
            body.TotalAmount,
            body.AfterWashPhotoUrl,
            body.CancelledBy);
    }

    private sealed class BookingBody
    {
        public string? ServiceName { get; set; }

        public decimal TotalAmount { get; set; }

        public string? AfterWashPhotoUrl { get; set; }

        public string? CancelledBy { get; set; }

        public List<AddOnBody>? AddOns { get; set; }
    }

    private sealed class AddOnBody
    {
        public string? Name { get; set; }

        public decimal Price { get; set; }
    }
}
