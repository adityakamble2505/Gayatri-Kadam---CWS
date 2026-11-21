using GreenCarWash.AuthService.DTOs;
using GreenCarWash.AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.AuthService.Controllers;

[ApiController]
[Route("api/auth/cities")]
public sealed class CitiesController : ControllerBase
{
    private readonly ICityService _cities;

    public CitiesController(ICityService cities)
    {
        _cities = cities;
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CityResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CityResponseDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _cities.ListActiveAsync(cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CityResponseDto>> Create(CityWriteDto dto, CancellationToken cancellationToken)
    {
        var city = await _cities.CreateAsync(dto, cancellationToken);
        return Created($"/api/auth/cities/{city.CityId}", city);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CityResponseDto>> Update(int id, CityWriteDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _cities.UpdateAsync(id, dto, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CityResponseDto>> Activate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _cities.SetActiveAsync(id, true, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CityResponseDto>> Deactivate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _cities.SetActiveAsync(id, false, cancellationToken));
    }
}
