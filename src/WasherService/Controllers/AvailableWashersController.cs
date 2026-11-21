using GreenCarWash.WasherService.Auth;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.WasherService.Controllers;

[ApiController]
[Authorize(Policy = AdminOrServiceHandler.PolicyName)]
[Route("api/washer/available")]
public sealed class AvailableWashersController : ControllerBase
{
    private readonly IWasherAdminService _admin;

    public AvailableWashersController(IWasherAdminService admin)
    {
        _admin = admin;
    }

    [HttpGet("{cityId:int}")]
    public async Task<ActionResult<IReadOnlyList<NearbyWasherDto>>> List(
        int cityId,
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        CancellationToken cancellationToken)
    {
        return Ok(await _admin.NearbyAsync(cityId, latitude, longitude, cancellationToken));
    }
}
