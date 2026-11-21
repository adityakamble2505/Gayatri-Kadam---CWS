using System.Globalization;
using System.Security.Claims;
using GreenCarWash.BuildingBlocks.Auth;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Pagination;
using GreenCarWash.NotificationService.DTOs;
using GreenCarWash.NotificationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.NotificationService.Controllers;

[ApiController]
[Authorize(Roles = "Customer,Washer,Admin")]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationCenter _notifications;

    public NotificationsController(INotificationCenter notifications)
    {
        _notifications = notifications;
    }

    [HttpGet("my")]
    public async Task<ActionResult<PagedResult<NotificationDto>>> My(
        [FromQuery] bool unreadOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _notifications.MyAsync(UserId(), unreadOnly, page, pageSize, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken cancellationToken)
    {
        return Ok(await _notifications.UnreadCountAsync(UserId(), cancellationToken));
    }

    [HttpPut("{id:long}/read")]
    public async Task<ActionResult<NotificationDto>> Read(long id, CancellationToken cancellationToken)
    {
        return Ok(await _notifications.MarkReadAsync(UserId(), id, cancellationToken));
    }

    [HttpPut("read-all")]
    public async Task<ActionResult<UnreadCountDto>> ReadAll(CancellationToken cancellationToken)
    {
        return Ok(await _notifications.MarkAllReadAsync(UserId(), cancellationToken));
    }

    [HttpGet("admin/email-logs")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedResult<EmailLogDto>>> EmailLogs(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _notifications.EmailLogsAsync(status, page, pageSize, cancellationToken));
    }

    [HttpPost("admin/email-logs/{id:long}/retry")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmailLogDto>> Retry(long id, CancellationToken cancellationToken)
    {
        return Ok(await _notifications.RetryEmailAsync(id, cancellationToken));
    }

    private int UserId()
    {
        var raw = User.FindFirst("userId")?.Value;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            throw new UnauthorizedException("Authentication is required.");
        return userId;
    }
}

[ApiController]
[Authorize(Policy = ServiceOnlyAuthorizationHandler.PolicyName)]
[Route("api/notifications")]
public sealed class NotificationsInternalController : ControllerBase
{
    private readonly INotificationCenter _notifications;

    public NotificationsInternalController(INotificationCenter notifications)
    {
        _notifications = notifications;
    }

    [HttpPost("internal/events")]
    public async Task<IActionResult> Events(NotificationEventDto dto, CancellationToken cancellationToken)
    {
        await _notifications.AcceptAsync(dto, cancellationToken);
        return Ok();
    }

    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail(SendEmailDto dto, CancellationToken cancellationToken)
    {
        await _notifications.QueueEmailAsync(dto, cancellationToken);
        return Ok();
    }
}
