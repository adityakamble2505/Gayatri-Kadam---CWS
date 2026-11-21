using GreenCarWash.PaymentService.DTOs;
using GreenCarWash.PaymentService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.PaymentService.Controllers;

[ApiController]
[Authorize]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentProcessor _payments;

    public PaymentsController(IPaymentProcessor payments)
    {
        _payments = payments;
    }

    [HttpPost("process")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<PaymentDto>> Process(ProcessPaymentDto dto, CancellationToken cancellationToken)
    {
        var result = await _payments.ProcessAsync(CurrentUser.From(User), dto, cancellationToken);
        if (result.Replay || result.Payment.Status != "Success")
            return Ok(result.Payment);
        return Created($"/api/payments/booking/{result.Payment.BookingId}", result.Payment);
    }

    [HttpGet("booking/{bookingId:int}")]
    public async Task<ActionResult<PaymentDto>> ForBooking(int bookingId, CancellationToken cancellationToken)
    {
        return Ok(await _payments.GetForBookingAsync(CurrentUser.From(User), bookingId, cancellationToken));
    }

    [HttpGet("booking/{bookingId:int}/receipt")]
    public async Task<ActionResult<PaymentReceiptDto>> Receipt(int bookingId, CancellationToken cancellationToken)
    {
        return Ok(await _payments.ReceiptAsync(CurrentUser.From(User), bookingId, cancellationToken));
    }

    [HttpPost("refund")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RefundDto>> Refund(RefundRequestDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _payments.RefundAsync(CurrentUser.From(User), dto, cancellationToken));
    }
}
