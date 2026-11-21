using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CustomerService.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/customer/vehicles")]
public sealed class VehiclesController : ControllerBase
{
    private readonly IVehicleService _vehicles;

    public VehiclesController(IVehicleService vehicles)
    {
        _vehicles = vehicles;
    }

    [HttpPost]
    public async Task<ActionResult<VehicleDto>> Create(VehicleWriteDto dto, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicles.AddAsync(CurrentUser.Id(User), dto, cancellationToken);
        return Created($"/api/customer/vehicles/{vehicle.VehicleId}", vehicle);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehicleDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _vehicles.ListActiveAsync(CurrentUser.Id(User), cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _vehicles.DeleteAsync(CurrentUser.Id(User), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/image")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<ActionResult<VehicleDto>> Upload(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw Input.Invalid("Image", "Image file is required.");
        if (file.Length > ImageSignatures.MaxBytes)
            throw Input.Invalid("Image", "Image must be a JPG or PNG up to 2 MB.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var vehicle = await _vehicles.SaveImageAsync(
            CurrentUser.Id(User),
            id,
            file.ContentType,
            buffer.ToArray(),
            cancellationToken);
        return Ok(vehicle);
    }
}
