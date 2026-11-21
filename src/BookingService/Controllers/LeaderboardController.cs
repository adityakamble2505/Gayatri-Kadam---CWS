using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize(Roles = "Customer,Admin")]
[Route("api/bookings/leaderboard")]
public sealed class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboard;

    public LeaderboardController(ILeaderboardService leaderboard)
    {
        _leaderboard = leaderboard;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LeaderboardEntryDto>>> Top(
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _leaderboard.TopAsync(top, cancellationToken));
    }
}
