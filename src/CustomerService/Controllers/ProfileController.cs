using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.CustomerService.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/customer/profile")]
public sealed class ProfileController : ControllerBase
{
    private readonly IProfileService _profiles;

    public ProfileController(IProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpPost]
    public async Task<ActionResult<ProfileDto>> Create(ProfileWriteDto dto, CancellationToken cancellationToken)
    {
        var profile = await _profiles.CreateAsync(CurrentUser.Id(User), dto, cancellationToken);
        return Created("/api/customer/profile", profile);
    }

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _profiles.GetAsync(CurrentUser.Id(User), cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update(ProfileWriteDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _profiles.UpdateAsync(CurrentUser.Id(User), dto, cancellationToken));
    }
}
