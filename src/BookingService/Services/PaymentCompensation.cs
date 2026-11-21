using System.Net;
using System.Net.Http.Json;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.BookingService.Services;

public interface IPaymentCompensation
{
    Task RefundOnCancelAsync(Booking booking, CancellationToken cancellationToken);

    Task<bool> TryRefundAsync(int bookingId, CancellationToken cancellationToken);
}

public sealed class NullPaymentCompensation : IPaymentCompensation
{
    public static readonly NullPaymentCompensation Instance = new();

    public Task RefundOnCancelAsync(Booking booking, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<bool> TryRefundAsync(int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

public sealed class PaymentCompensation : IPaymentCompensation
{
    private readonly HttpClient _http;
    private readonly IBookingRepository _bookings;
    private readonly IClock _clock;
    private readonly ILogger<PaymentCompensation> _logger;

    public PaymentCompensation(
        HttpClient http,
        IBookingRepository bookings,
        IClock clock,
        ILogger<PaymentCompensation> logger)
    {
        _http = http;
        _bookings = bookings;
        _clock = clock;
        _logger = logger;
    }

    public async Task RefundOnCancelAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (!await TryRefundAsync(booking.BookingId, cancellationToken))
            await RecordPendingAsync(booking, cancellationToken);
    }

    public async Task<bool> TryRefundAsync(int bookingId, CancellationToken cancellationToken)
    {
        HttpResponseMessage lookup;
        try
        {
            lookup = await _http.GetAsync($"api/payments/internal/by-booking/{bookingId}", cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Refund lookup failed for booking {BookingId}", bookingId);
            return false;
        }

        if (lookup.StatusCode == HttpStatusCode.NotFound)
            return true;

        if (!lookup.IsSuccessStatusCode)
        {
            _logger.LogWarning("Refund lookup returned {StatusCode} for booking {BookingId}", (int)lookup.StatusCode, bookingId);
            return false;
        }

        var body = await lookup.Content.ReadFromJsonAsync<PaymentLookup>(cancellationToken);
        if (body?.Status is not ("Success" or "PartiallyRefunded"))
            return true;

        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/payments/internal/refund-for-booking",
                new { bookingId },
                cancellationToken);
            await Downstream.EnsureSuccessAsync(response, "Payment refund");
            return true;
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Refund call failed for booking {BookingId}", bookingId);
            return false;
        }
    }

    private async Task RecordPendingAsync(Booking booking, CancellationToken cancellationToken)
    {
        try
        {
            await _bookings.AppendEventAsync(booking.BookingId, "RefundPending", _clock.UtcNow, cancellationToken);
        }
        catch (Exception writeFailure) when (!IsCallerCancellation(writeFailure, cancellationToken))
        {
            _logger.LogWarning(writeFailure, "Could not store RefundPending for booking {BookingId}", booking.BookingId);
        }
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private sealed class PaymentLookup
    {
        public string? Status { get; set; }
    }
}
