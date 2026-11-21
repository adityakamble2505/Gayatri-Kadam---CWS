using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly IBookingWorkflow _bookings;

    public BookingsController(IBookingWorkflow bookings)
    {
        _bookings = bookings;
    }

    [HttpPost("wash-now")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<BookingDetailDto>> WashNow(CreateBookingDto dto, CancellationToken cancellationToken)
    {
        var booking = await _bookings.WashNowAsync(CurrentUser.From(User), dto, cancellationToken);
        return Created($"/api/bookings/{booking.BookingId}", booking);
    }

    [HttpPost("schedule")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<BookingDetailDto>> Schedule(ScheduleBookingDto dto, CancellationToken cancellationToken)
    {
        var booking = await _bookings.ScheduleAsync(CurrentUser.From(User), dto, cancellationToken);
        return Created($"/api/bookings/{booking.BookingId}", booking);
    }

    [HttpGet("my-orders")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult> MyOrders([FromQuery] OrderListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.MyOrdersAsync(CurrentUser.From(User), query, cancellationToken));
    }

    [HttpGet("washer-orders")]
    [Authorize(Roles = "Washer")]
    public async Task<ActionResult> WasherOrders([FromQuery] OrderListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.WasherOrdersAsync(CurrentUser.From(User), query, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingDetailDto>> Get(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.GetAsync(CurrentUser.From(User), id, cancellationToken));
    }

    [HttpPut("{id:int}/accept")]
    [Authorize(Roles = "Washer")]
    public async Task<ActionResult<BookingDetailDto>> Accept(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.AcceptAsync(CurrentUser.From(User), id, cancellationToken));
    }

    [HttpPut("{id:int}/decline")]
    [Authorize(Roles = "Washer")]
    public async Task<IActionResult> Decline(int id, CancellationToken cancellationToken)
    {
        await _bookings.DeclineAsync(CurrentUser.From(User), id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:int}/start")]
    [Authorize(Roles = "Washer")]
    public async Task<ActionResult<BookingDetailDto>> Start(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.StartAsync(CurrentUser.From(User), id, cancellationToken));
    }

    [HttpPut("{id:int}/complete")]
    [Authorize(Roles = "Washer")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<BookingDetailDto>> Complete(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw Input.Invalid("Image", "After-wash photo is required.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var booking = await _bookings.CompleteAsync(CurrentUser.From(User), id, file.ContentType, buffer.ToArray(), cancellationToken);
        return Ok(booking);
    }

    [HttpPut("{id:int}/assign")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BookingDetailDto>> Assign(int id, AssignWasherDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.AssignAsync(id, dto.WasherId, CurrentUser.Id(User), cancellationToken));
    }

    [HttpGet("admin/attention")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<BookingSummaryDto>>> Attention(CancellationToken cancellationToken)
    {
        return Ok(await _bookings.AttentionAsync(cancellationToken));
    }

    [HttpPut("{id:int}/cancel")]
    public async Task<ActionResult<BookingDetailDto>> Cancel(int id, CancelBookingDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.CancelAsync(CurrentUser.From(User), id, dto, cancellationToken));
    }
}
