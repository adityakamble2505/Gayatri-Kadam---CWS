using ClosedXML.Excel;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Pagination;

namespace GreenCarWash.BookingService.Services;

public interface IAdminOrderService
{
    Task<PagedResult<AdminOrderDto>> ListAsync(AdminOrderQuery criteria, CancellationToken cancellationToken);

    Task<byte[]> ExportAsync(AdminOrderQuery criteria, CancellationToken cancellationToken);
}

public sealed class AdminOrderService : IAdminOrderService
{
    public const int ExportCap = 10_000;

    private readonly IBookingInsightsRepository _insights;

    public AdminOrderService(IBookingInsightsRepository insights)
    {
        _insights = insights;
    }

    public async Task<PagedResult<AdminOrderDto>> ListAsync(AdminOrderQuery criteria, CancellationToken cancellationToken)
    {
        var (page, pageSize, skip) = Pagination.Normalize(criteria.Page, criteria.PageSize);
        var (items, total) = await _insights.OrdersAsync(criteria, skip, pageSize, cancellationToken);
        return Pagination.Create(items.Select(ToDto).ToArray(), page, pageSize, total);
    }

    public async Task<byte[]> ExportAsync(AdminOrderQuery criteria, CancellationToken cancellationToken)
    {
        var rows = await _insights.ExportOrdersAsync(criteria, ExportCap, cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Orders");
        var headers = new[]
        {
            "BookingId", "BookingNumber", "CustomerId", "WasherId", "ServiceId", "ServiceName",
            "BookingType", "Status", "BookingDate", "TotalAmount", "CityId"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        for (var index = 0; index < rows.Count; index++)
        {
            var order = rows[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = order.BookingId;
            sheet.Cell(row, 2).Value = order.BookingNumber;
            sheet.Cell(row, 3).Value = order.CustomerId;
            sheet.Cell(row, 4).Value = order.WasherId?.ToString() ?? string.Empty;
            sheet.Cell(row, 5).Value = order.ServiceId;
            sheet.Cell(row, 6).Value = order.ServiceNameSnapshot;
            sheet.Cell(row, 7).Value = order.BookingType.ToString();
            sheet.Cell(row, 8).Value = order.Status.ToString();
            sheet.Cell(row, 9).Value = order.BookingDate;
            sheet.Cell(row, 10).Value = order.TotalAmount;
            sheet.Cell(row, 11).Value = order.CityId;
        }

        sheet.Row(1).Style.Font.Bold = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static AdminOrderDto ToDto(Booking booking) => new()
    {
        BookingId = booking.BookingId,
        BookingNumber = booking.BookingNumber,
        CustomerId = booking.CustomerId,
        WasherId = booking.WasherId,
        ServiceId = booking.ServiceId,
        ServiceName = booking.ServiceNameSnapshot,
        BookingType = booking.BookingType.ToString(),
        Status = booking.Status.ToString(),
        BookingDate = booking.BookingDate,
        TotalAmount = booking.TotalAmount,
        CityId = booking.CityId
    };
}
