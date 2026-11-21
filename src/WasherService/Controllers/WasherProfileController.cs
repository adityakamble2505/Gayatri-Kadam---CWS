using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.WasherService.Controllers;

[ApiController]
[Authorize(Roles = "Washer")]
[Route("api/washer")]
public sealed class WasherProfileController : ControllerBase
{
    private readonly IWasherProfileService _profiles;

    public WasherProfileController(IWasherProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpPost("profile")]
    public async Task<ActionResult<WasherResponseDto>> Create(CreateWasherProfileDto dto, CancellationToken cancellationToken)
    {
        var washer = await _profiles.CreateAsync(CurrentUser.Id(User), dto, cancellationToken);
        return Created("/api/washer/profile", washer);
    }

    [HttpGet("profile")]
    public async Task<ActionResult<WasherResponseDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _profiles.GetAsync(CurrentUser.Id(User), cancellationToken));
    }

    [HttpPut("availability")]
    public async Task<ActionResult<WasherResponseDto>> Availability(AvailabilityDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _profiles.SetAvailabilityAsync(CurrentUser.Id(User), dto, cancellationToken));
    }

    [HttpPut("location")]
    public async Task<ActionResult<WasherResponseDto>> Location(LocationDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _profiles.SetLocationAsync(CurrentUser.Id(User), dto, cancellationToken));
    }

    [HttpPost("profile/photo")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<ActionResult<WasherResponseDto>> Photo(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw Input.Invalid("Image", "Image file is required.");
        if (file.Length > ImageSignatures.MaxBytes)
            throw Input.Invalid("Image", "Image must be a JPG or PNG up to 2 MB.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var washer = await _profiles.SavePhotoAsync(CurrentUser.Id(User), file.ContentType, buffer.ToArray(), cancellationToken);
        return Ok(washer);
    }
}
