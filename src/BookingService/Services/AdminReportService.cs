using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Repositories;

namespace GreenCarWash.BookingService.Services;

public interface IAdminReportService
{
    Task<IReadOnlyList<DailyRevenueDto>> DailyRevenueAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopServiceDto>> TopServicesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<PeakHourDto>> PeakHoursAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CancellationReportDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<AddOnRevenueDto>> AddOnRevenueAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<WasherPerformanceDto>> WasherPerformanceAsync(ReportWindow window, CancellationToken cancellationToken);
}

public sealed class AdminReportService : IAdminReportService
{
    private readonly IAdminReportRepository _reports;
    private readonly DisplayNameLookup _names;

    public AdminReportService(IAdminReportRepository reports, DisplayNameLookup names)
    {
        _reports = reports;
        _names = names;
    }

    public Task<IReadOnlyList<DailyRevenueDto>> DailyRevenueAsync(ReportWindow window, CancellationToken cancellationToken) =>
        _reports.DailyRevenueAsync(window, cancellationToken);

    public Task<IReadOnlyList<TopServiceDto>> TopServicesAsync(ReportWindow window, CancellationToken cancellationToken) =>
        _reports.TopServicesAsync(window, cancellationToken);

    public Task<IReadOnlyList<PeakHourDto>> PeakHoursAsync(ReportWindow window, CancellationToken cancellationToken) =>
        _reports.PeakHoursAsync(window, cancellationToken);

    public Task<IReadOnlyList<CancellationReportDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken) =>
        _reports.CancellationsAsync(window, cancellationToken);

    public Task<IReadOnlyList<AddOnRevenueDto>> AddOnRevenueAsync(ReportWindow window, CancellationToken cancellationToken) =>
        _reports.AddOnRevenueAsync(window, cancellationToken);

    public async Task<IReadOnlyList<WasherPerformanceDto>> WasherPerformanceAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var rows = await _reports.WasherPerformanceAsync(window, cancellationToken);
        var named = new List<WasherPerformanceDto>(rows.Count);
        foreach (var row in rows)
        {
            named.Add(new WasherPerformanceDto
            {
                WasherId = row.WasherId,
                WasherName = await _names.WasherAsync(row.WasherId, cancellationToken),
                CompletedCount = row.CompletedCount,
                Revenue = row.Revenue,
                AvgMinutes = row.AvgMinutes
            });
        }

        return named;
    }
}
