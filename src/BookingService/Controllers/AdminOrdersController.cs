using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/bookings/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IAdminOrderService _orders;

    public AdminOrdersController(IAdminOrderService orders)
    {
        _orders = orders;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminOrderDto>>> List(
        [FromQuery] AdminOrderQuery query,
        CancellationToken cancellationToken)
    {
        return Ok(await _orders.ListAsync(query, cancellationToken));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken)
    {
        var bytes = await _orders.ExportAsync(query, cancellationToken);
        return File(bytes, ExcelContentType, "orders.xlsx");
    }
}
