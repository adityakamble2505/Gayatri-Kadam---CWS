using System.Net.Http.Json;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.BookingService.Services;

public sealed class CustomerSnapshot
{
    public int CustomerId { get; set; }

    public int UserId { get; set; }

    public int CityId { get; set; }

    public string Address { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public sealed class VehicleSnapshot
{
    public int VehicleId { get; set; }

    public int CustomerId { get; set; }
}

public sealed class WasherSnapshot
{
    public int WasherId { get; set; }

    public int UserId { get; set; }

    public int CityId { get; set; }

    public bool IsActive { get; set; }

    public bool IsQualified { get; set; }
}

public sealed record WasherOffer(int WasherId, int UserId);

public sealed class QuoteAddOnSnapshot
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

public sealed class QuoteSnapshot
{
    public string ServiceNameSnapshot { get; set; } = string.Empty;

    public decimal ServicePriceSnapshot { get; set; }

    public int DiscountPct { get; set; }

    public decimal AddOnsTotal { get; set; }

    public List<QuoteAddOnSnapshot> AddOns { get; set; } = [];

    public decimal PromoDiscount { get; set; }

    public decimal TotalAmount { get; set; }

    public int DurationMin { get; set; }

    public int WaterSavedLiters { get; set; }
}

public interface ICustomerDirectory
{
    Task<CustomerSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken);

    Task<CustomerSnapshot> GetByIdAsync(int customerId, CancellationToken cancellationToken);

    Task<VehicleSnapshot> GetVehicleAsync(int customerId, int vehicleId, CancellationToken cancellationToken);
}

public interface IWasherDirectory
{
    Task<WasherSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken);

    Task<WasherSnapshot> GetByIdAsync(int washerId, CancellationToken cancellationToken);

    Task<WasherOffer> NextAsync(
        int cityId,
        IReadOnlyCollection<int> excludeIds,
        string? strategy,
        decimal? latitude,
        decimal? longitude,
        CancellationToken cancellationToken);

    Task StageAssignedAsync(int washerId, CancellationToken cancellationToken);

    Task ConfirmAsync(int washerId, int bookingId, CancellationToken cancellationToken);

    Task StartAsync(int washerId, CancellationToken cancellationToken);

    Task CompleteAsync(int washerId, CancellationToken cancellationToken);

    Task ReleaseOfferAsync(int washerId, CancellationToken cancellationToken);

    Task MakeAvailableAsync(int washerId, CancellationToken cancellationToken);

    Task RateAsync(int washerId, int rating, CancellationToken cancellationToken);
}

public interface ICatalogPricing
{
    Task<QuoteSnapshot> QuoteAsync(int serviceId, IReadOnlyList<int> addOnIds, string? promoCode, CancellationToken cancellationToken);

    Task RedeemAsync(string code, decimal subtotal, CancellationToken cancellationToken);

    Task ReleaseAsync(string code, CancellationToken cancellationToken);
}

public sealed class CustomerDirectory : ICustomerDirectory
{
    private readonly HttpClient _http;

    public CustomerDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<CustomerSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/customer/internal/by-user/{userId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Customer");
        return (await response.Content.ReadFromJsonAsync<CustomerSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Customer service returned an empty profile.");
    }

    public async Task<CustomerSnapshot> GetByIdAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/customer/internal/{customerId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Customer");
        return (await response.Content.ReadFromJsonAsync<CustomerSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Customer service returned an empty profile.");
    }

    public async Task<VehicleSnapshot> GetVehicleAsync(int customerId, int vehicleId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/customer/internal/{customerId}/vehicles/{vehicleId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Vehicle");
        return (await response.Content.ReadFromJsonAsync<VehicleSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Customer service returned an empty vehicle.");
    }
}

public sealed class WasherDirectory : IWasherDirectory
{
    private readonly HttpClient _http;

    public WasherDirectory(HttpClient http)
    {
        _http = http;
    }

    public async Task<WasherSnapshot> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/washer/internal/by-user/{userId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer");
        return (await response.Content.ReadFromJsonAsync<WasherSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Washer service returned an empty profile.");
    }

    public async Task<WasherSnapshot> GetByIdAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"api/washer/internal/{washerId}", cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer");
        return (await response.Content.ReadFromJsonAsync<WasherSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Washer service returned an empty profile.");
    }

    public async Task<WasherOffer> NextAsync(
        int cityId,
        IReadOnlyCollection<int> excludeIds,
        string? strategy,
        decimal? latitude,
        decimal? longitude,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/washer/internal/queue/next",
            new { cityId, excludeIds, strategy, latitude, longitude },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer queue");
        var offered = await response.Content.ReadFromJsonAsync<WasherOffer>(cancellationToken);
        return offered is null || offered.WasherId <= 0
            ? throw new BadGatewayException("Washer queue returned an empty offer.")
            : offered;
    }

    public async Task StageAssignedAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync(
            "api/washer/internal/assignment",
            new { washerId, assignmentStatus = "Assigned", clearBooking = false },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer assignment");
    }

    public async Task ConfirmAsync(int washerId, int bookingId, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/washer/internal/assignment/confirm",
            new { washerId, bookingId },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer assignment");
    }

    public async Task StartAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/washer/internal/assignment/start",
            new { washerId },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer assignment");
    }

    public async Task CompleteAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/washer/internal/assignment/complete",
            new { washerId },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer assignment");
    }

    public async Task ReleaseOfferAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/washer/internal/queue/release",
            new { washerId, toTail = true },
            cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound)
            return;
        await Downstream.EnsureSuccessAsync(response, "Washer queue");
    }

    public async Task MakeAvailableAsync(int washerId, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync(
            "api/washer/internal/assignment",
            new { washerId, assignmentStatus = "Available", clearBooking = true },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer assignment");
    }

    public async Task RateAsync(int washerId, int rating, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync(
            "api/washer/internal/rating",
            new { washerId, rating },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Washer rating");
    }
}

public sealed class CatalogPricing : ICatalogPricing
{
    private readonly HttpClient _http;

    public CatalogPricing(HttpClient http)
    {
        _http = http;
    }

    public async Task<QuoteSnapshot> QuoteAsync(int serviceId, IReadOnlyList<int> addOnIds, string? promoCode, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/catalog/internal/quote",
            new { serviceId, addOnIds, promoCode },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Quote");
        return (await response.Content.ReadFromJsonAsync<QuoteSnapshot>(cancellationToken))
            ?? throw new BadGatewayException("Catalog service returned an empty quote.");
    }

    public async Task RedeemAsync(string code, decimal subtotal, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/catalog/internal/promos/redeem",
            new { code, subtotal },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Promo");
    }

    public async Task ReleaseAsync(string code, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "api/catalog/internal/promos/release",
            new { code },
            cancellationToken);
        await Downstream.EnsureSuccessAsync(response, "Promo");
    }
}

internal static class Downstream
{
    public static Task EnsureSuccessAsync(HttpResponseMessage response, string resource)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new NotFoundException(resource, resource);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new ConflictException($"{resource} request was rejected.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw Input.Invalid(resource, $"{resource} request was rejected.");
        throw new BadGatewayException($"{resource} service call failed.");
    }
}
