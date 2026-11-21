using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CatalogService.Controllers;

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/catalog/internal")]
public sealed class CatalogInternalController : ControllerBase
{
    private readonly IPromoCatalog _promos;
    private readonly IQuoteService _quotes;

    public CatalogInternalController(IPromoCatalog promos, IQuoteService quotes)
    {
        _promos = promos;
        _quotes = quotes;
    }

    [HttpPost("promos/redeem")]
    public async Task<ActionResult<PromoRedeemResultDto>> Redeem(PromoCheckDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _promos.RedeemAsync(dto, cancellationToken));
    }

    [HttpPost("promos/release")]
    public async Task<ActionResult<PromoReleaseResultDto>> Release(PromoReleaseDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _promos.ReleaseAsync(dto, cancellationToken));
    }

    [HttpPost("quote")]
    public async Task<ActionResult<QuoteResponseDto>> Quote(QuoteRequestDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _quotes.QuoteAsync(dto, cancellationToken));
    }
}
