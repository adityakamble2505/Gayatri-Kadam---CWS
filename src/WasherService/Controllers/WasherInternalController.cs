using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.WasherService.Controllers;

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/washer/internal")]
public sealed class WasherInternalController : ControllerBase
{
    private readonly IWasherDispatchService _dispatch;

    public WasherInternalController(IWasherDispatchService dispatch)
    {
        _dispatch = dispatch;
    }

    [HttpPost("queue/next")]
    public async Task<ActionResult<NextWasherResultDto>> Next(NextWasherDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _dispatch.NextAsync(dto, cancellationToken));
    }

    [HttpPost("queue/release")]
    public async Task<IActionResult> Release(ReleaseWasherDto dto, CancellationToken cancellationToken)
    {
        await _dispatch.ReleaseAsync(dto, cancellationToken);
        return Ok();
    }

    [HttpPost("assignment/confirm")]
    public async Task<IActionResult> Confirm(ConfirmAssignmentDto dto, CancellationToken cancellationToken)
    {
        await _dispatch.ConfirmAsync(dto, cancellationToken);
        return Ok();
    }

    [HttpPost("assignment/start")]
    public async Task<IActionResult> Start(WasherIdDto dto, CancellationToken cancellationToken)
    {
        await _dispatch.StartAsync(dto, cancellationToken);
        return Ok();
    }

    [HttpPost("assignment/complete")]
    public async Task<IActionResult> Complete(WasherIdDto dto, CancellationToken cancellationToken)
    {
        await _dispatch.CompleteAsync(dto, cancellationToken);
        return Ok();
    }

    [HttpPut("assignment")]
    public async Task<ActionResult<WasherResponseDto>> Assignment(AssignmentUpdateDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _dispatch.UpdateAssignmentAsync(dto, cancellationToken));
    }

    [HttpPut("rating")]
    public async Task<ActionResult<WasherResponseDto>> Rating(RatingUpdateDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _dispatch.RateAsync(dto, cancellationToken));
    }

    [HttpGet("by-user/{userId:int}")]
    public async Task<ActionResult<WasherResponseDto>> ByUser(int userId, CancellationToken cancellationToken)
    {
        return Ok(await _dispatch.GetByUserAsync(userId, cancellationToken));
    }

    [HttpGet("{washerId:int}")]
    public async Task<ActionResult<WasherResponseDto>> Get(int washerId, CancellationToken cancellationToken)
    {
        return Ok(await _dispatch.GetAsync(washerId, cancellationToken));
    }
}
