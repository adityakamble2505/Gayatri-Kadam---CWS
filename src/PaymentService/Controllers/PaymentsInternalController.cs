using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.PaymentService.DTOs;
using GreenCarWash.PaymentService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.PaymentService.Controllers;

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/payments/internal")]
public sealed class PaymentsInternalController : ControllerBase
{
    private readonly IPaymentProcessor _payments;

    public PaymentsInternalController(IPaymentProcessor payments)
    {
        _payments = payments;
    }

    [HttpGet("by-booking/{bookingId:int}")]
    public async Task<ActionResult<PaymentDto>> ByBooking(int bookingId, CancellationToken cancellationToken)
    {
        return Ok(await _payments.GetInternalByBookingAsync(bookingId, cancellationToken));
    }

    [HttpPost("refund-for-booking")]
    public async Task<ActionResult<RefundNoticeDto>> RefundForBooking(BookingRefundRequest dto, CancellationToken cancellationToken)
    {
        return Ok(await _payments.RefundForCancelledBookingAsync(dto.BookingId, cancellationToken));
    }
}
