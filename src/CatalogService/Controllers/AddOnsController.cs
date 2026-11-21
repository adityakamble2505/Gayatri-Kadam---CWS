using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CatalogService.Controllers;

[ApiController]
[Route("api/catalog/addons")]
public sealed class AddOnsController : ControllerBase
{
    private readonly IAddOnCatalog _addOns;

    public AddOnsController(IAddOnCatalog addOns)
    {
        _addOns = addOns;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AddOnResponseDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _addOns.ListActiveAsync(cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<AddOnResponseDto>> Create(AddOnWriteDto dto, CancellationToken cancellationToken)
    {
        var addOn = await _addOns.CreateAsync(dto, cancellationToken);
        return Created($"/api/catalog/addons/{addOn.AddOnId}", addOn);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AddOnResponseDto>> Update(int id, AddOnWriteDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _addOns.UpdateAsync(id, dto, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/activate")]
    public async Task<ActionResult<AddOnResponseDto>> Activate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _addOns.SetActiveAsync(id, true, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/deactivate")]
    public async Task<ActionResult<AddOnResponseDto>> Deactivate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _addOns.SetActiveAsync(id, false, cancellationToken));
    }
}
