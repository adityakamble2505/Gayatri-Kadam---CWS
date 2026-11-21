using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CatalogService.Controllers;

[ApiController]
[Route("api/catalog/promos")]
public sealed class PromosController : ControllerBase
{
    private readonly IPromoCatalog _promos;

    public PromosController(IPromoCatalog promos)
    {
        _promos = promos;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PromoResponseDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _promos.ListAsync(cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PromoResponseDto>> Get(int id, CancellationToken cancellationToken)
    {
        return Ok(await _promos.GetAsync(id, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<PromoResponseDto>> Create(PromoWriteDto dto, CancellationToken cancellationToken)
    {
        var promo = await _promos.CreateAsync(dto, cancellationToken);
        return Created($"/api/catalog/promos/{promo.PromoId}", promo);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PromoResponseDto>> Update(int id, PromoWriteDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _promos.UpdateAsync(id, dto, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/activate")]
    public async Task<ActionResult<PromoResponseDto>> Activate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _promos.SetActiveAsync(id, true, cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/deactivate")]
    public async Task<ActionResult<PromoResponseDto>> Deactivate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _promos.SetActiveAsync(id, false, cancellationToken));
    }

    [Authorize(Roles = "Customer")]
    [HttpPost("validate")]
    public async Task<ActionResult<PromoDiscountDto>> Validate(PromoCheckDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _promos.ValidateAsync(dto, cancellationToken));
    }
}
