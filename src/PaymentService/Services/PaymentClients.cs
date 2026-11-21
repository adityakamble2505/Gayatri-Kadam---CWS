using System.Net.Http.Json;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.PaymentService.Services;

public sealed class BookingPaymentSnapshot
{
    public int BookingId { get; set; }

    public string BookingNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public int CustomerId { get; set; }

    public int CustomerUserId { get; set; }

    public int? WasherId { get; set; }

    public int? WasherUserId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string? AfterWashPhotoUrl { get; set; }

    public List<BookingAddOnSnapshot> AddOns { get; set; } = [];
}

public sealed class BookingAddOnSnapshot
{
    public int AddOnId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

public sealed class CustomerPaymentSnapshot
{
    public int CustomerId { get; set; }

    public int UserId { get; set; }
}

public sealed class WasherPaymentSnapshot
{
    public int WasherId { get; set; }

    public int UserId { get; set; }
}

public interface IBookingDirectory
{
    Task<BookingPaymentSnapshot> GetAsync(int bookingId, CancellationToken cancellationToken);
}

public interface ICustomerDirectory
{
    Task<CustomerPaymentSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken);
}

public interface IWasherDirectory
{
    Task<WasherPaymentSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken);
}

public sealed class BookingDirectory : IBookingDirectory
{
    private readonly HttpClient _http;

    public BookingDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<BookingPaymentSnapshot> GetAsync(int bookingId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/bookings/internal/{bookingId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Booking");
        return (await response.Content.ReadFromJsonAsync<BookingPaymentSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Booking service returned an empty booking.");
    }
}

public sealed class CustomerDirectory : ICustomerDirectory
{
    private readonly HttpClient _http;

    public CustomerDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<CustomerPaymentSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/customer/internal/by-user/{userId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Customer");
        return (await response.Content.ReadFromJsonAsync<CustomerPaymentSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Customer service returned an empty profile.");
    }
}

public sealed class WasherDirectory : IWasherDirectory
{
    private readonly HttpClient _http;

    public WasherDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<WasherPaymentSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/washer/internal/by-user/{userId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer");
        return (await response.Content.ReadFromJsonAsync<WasherPaymentSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Washer service returned an empty profile.");
    }
}

internal static class Downstream
{
    public static Task EnsureSuccessAsync(HttpResponseMessage response, string dependency)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new NotFoundException($"{dependency} was not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new ConflictException($"{dependency} rejected the request.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new FluentValidation.ValidationException($"{dependency} rejected the request.");

        throw new BadGatewayException($"{dependency} is unavailable.");
    }
}
