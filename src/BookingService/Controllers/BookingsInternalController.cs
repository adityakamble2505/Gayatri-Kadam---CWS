using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/bookings/internal")]
public sealed class BookingsInternalController : ControllerBase
{
    private readonly IBookingWorkflow _bookings;

    public BookingsInternalController(IBookingWorkflow bookings)
    {
        _bookings = bookings;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InternalBookingDto>> Get(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookings.GetInternalAsync(id, cancellationToken));
    }
}
