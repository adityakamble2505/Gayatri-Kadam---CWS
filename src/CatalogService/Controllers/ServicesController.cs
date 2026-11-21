using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CatalogService.Controllers;

[ApiController]
[Route("api/catalog/services")]
public sealed class ServicesController : ControllerBase
{
    private readonly IWashServiceCatalog _services;

    public ServicesController(IWashServiceCatalog services)
    {
        _services = services;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceResponseDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _services.ListActiveAsync(cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponseDto>> Get(int id, CancellationToken cancellationToken)
    {
        return Ok(await _services.GetAsync(id, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ServiceResponseDto>> Create(ServiceWriteDto dto, CancellationToken cancellationToken)
    {
        var service = await _services.CreateAsync(dto, cancellationToken);
        return Created($"/api/catalog/services/{service.ServiceId}", service);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceResponseDto>> Update(int id, ServiceWriteDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _services.UpdateAsync(id, dto, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/activate")]
    public async Task<ActionResult<ServiceResponseDto>> Activate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _services.SetActiveAsync(id, true, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/deactivate")]
    public async Task<ActionResult<ServiceResponseDto>> Deactivate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _services.SetActiveAsync(id, false, cancellationToken));
    }
}
