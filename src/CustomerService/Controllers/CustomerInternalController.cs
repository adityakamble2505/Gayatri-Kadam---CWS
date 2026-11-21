using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CustomerService.Controllers;

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/customer/internal")]
public sealed class CustomerInternalController : ControllerBase
{
    private readonly IVehicleService _vehicles;
    private readonly IProfileService _profiles;

    public CustomerInternalController(IVehicleService vehicles, IProfileService profiles)
    {
        _vehicles = vehicles;
        _profiles = profiles;
    }

    [HttpGet("{customerId:int}/vehicles/{vehicleId:int}")]
    public async Task<ActionResult<InternalVehicleDto>> Vehicle(int customerId, int vehicleId, CancellationToken cancellationToken)
    {
        return Ok(await _vehicles.GetForBookingAsync(customerId, vehicleId, cancellationToken));
    }

    [HttpGet("{customerId:int}")]
    public async Task<ActionResult<ProfileDto>> ById(int customerId, CancellationToken cancellationToken)
    {
        return Ok(await _profiles.GetByIdAsync(customerId, cancellationToken));
    }

    [HttpGet("by-user/{userId:int}")]
    public async Task<ActionResult<ProfileDto>> ByUser(int userId, CancellationToken cancellationToken)
    {
        return Ok(await _profiles.GetByUserIdAsync(userId, cancellationToken));
    }
}
