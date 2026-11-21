using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.WasherService.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/washer/admin")]
public sealed class WasherAdminController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IWasherAdminService _admin;

    public WasherAdminController(IWasherAdminService admin)
    {
        _admin = admin;
    }

    [HttpGet("washers")]
    public async Task<ActionResult<PagedWashersDto>> List(
        [FromQuery] int? cityId,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isAvailable,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _admin.ListAsync(cityId, isActive, isAvailable, page, pageSize, cancellationToken));
    }

    [HttpPost("washers")]
    public async Task<ActionResult<WasherResponseDto>> Create(AdminWasherWriteDto dto, CancellationToken cancellationToken)
    {
        var washer = await _admin.CreateAsync(dto, cancellationToken);
        return Created($"/api/washer/admin/washers/{washer.WasherId}", washer);
    }

    [HttpPut("washers/{id:int}")]
    public async Task<ActionResult<WasherResponseDto>> Update(int id, AdminWasherUpdateDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _admin.UpdateAsync(id, dto, cancellationToken));
    }

    [HttpPut("washers/{id:int}/activate")]
    public async Task<ActionResult<WasherResponseDto>> Activate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _admin.SetActiveAsync(id, true, cancellationToken));
    }

    [HttpPut("washers/{id:int}/deactivate")]
    public async Task<ActionResult<WasherResponseDto>> Deactivate(int id, CancellationToken cancellationToken)
    {
        return Ok(await _admin.SetActiveAsync(id, false, cancellationToken));
    }

    [HttpPut("washers/{id:int}/qualify")]
    public async Task<ActionResult<WasherResponseDto>> Qualify(int id, QualifyDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _admin.SetQualifiedAsync(id, dto.IsQualified, cancellationToken));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var bytes = await _admin.ExportAsync(cancellationToken);
        return File(bytes, ExcelContentType, "washers.xlsx");
    }

    [HttpGet("queue-snapshot")]
    public ActionResult<IReadOnlyList<QueueEntryDto>> Snapshot([FromQuery] int cityId)
    {
        return Ok(_admin.Snapshot(cityId));
    }
}
