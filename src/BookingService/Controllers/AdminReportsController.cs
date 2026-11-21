using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/bookings/admin/reports")]
public sealed class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _reports;

    public AdminReportsController(IAdminReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("daily-revenue")]
    public async Task<ActionResult<IReadOnlyList<DailyRevenueDto>>> DailyRevenue(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.DailyRevenueAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    [HttpGet("top-services")]
    public async Task<ActionResult<IReadOnlyList<TopServiceDto>>> TopServices(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.TopServicesAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    [HttpGet("peak-hours")]
    public async Task<ActionResult<IReadOnlyList<PeakHourDto>>> PeakHours(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.PeakHoursAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    [HttpGet("cancellations")]
    public async Task<ActionResult<IReadOnlyList<CancellationReportDto>>> Cancellations(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.CancellationsAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    [HttpGet("addon-revenue")]
    public async Task<ActionResult<IReadOnlyList<AddOnRevenueDto>>> AddOnRevenue(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.AddOnRevenueAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    [HttpGet("washer-performance")]
    public async Task<ActionResult<IReadOnlyList<WasherPerformanceDto>>> WasherPerformance(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? cityId,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.WasherPerformanceAsync(Window(dateFrom, dateTo, cityId), cancellationToken));
    }

    private static ReportWindow Window(DateTime? dateFrom, DateTime? dateTo, int? cityId) =>
        new() { DateFrom = dateFrom, DateTo = dateTo, CityId = cityId };
}
