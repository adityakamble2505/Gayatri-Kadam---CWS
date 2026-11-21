using System.Data;
using System.Data.Common;
using System.Globalization;
using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Repositories;

public interface IAdminReportRepository
{
    Task<IReadOnlyList<DailyRevenueDto>> DailyRevenueAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopServiceDto>> TopServicesAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<PeakHourDto>> PeakHoursAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<CancellationReportDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<AddOnRevenueDto>> AddOnRevenueAsync(ReportWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<WasherPerformanceDto>> WasherPerformanceAsync(ReportWindow window, CancellationToken cancellationToken);
}

public sealed class AdminReportRepository : IAdminReportRepository
{
    private readonly BookingDbContext _db;

    public AdminReportRepository(BookingDbContext db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<DailyRevenueDto>> DailyRevenueAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(window, "CompletedAt", "CityId", "Status = 'Completed' AND CompletedAt IS NOT NULL");
        var sql = $"""
            SELECT CAST(CompletedAt AS date) AS Day,
                   COUNT(*) AS CompletedCount,
                   SUM(TotalAmount) AS Revenue,
                   SUM(SUM(TotalAmount)) OVER (ORDER BY CAST(CompletedAt AS date)) AS RunningTotal
            FROM Bookings
            WHERE {where}
            GROUP BY CAST(CompletedAt AS date)
            ORDER BY Day
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new DailyRevenueDto
        {
            Day = reader.GetDateTime(0),
            CompletedCount = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            Revenue = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
            RunningTotal = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture)
        });
    }

    public Task<IReadOnlyList<TopServiceDto>> TopServicesAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(window, "BookingDate", "CityId", "Status <> 'Cancelled'");
        var sql = $"""
            SELECT CityId, ServiceId, ServiceName, BookingCount, rn
            FROM (
              SELECT CityId,
                     ServiceId,
                     MAX(ServiceNameSnapshot) AS ServiceName,
                     COUNT(*) AS BookingCount,
                     ROW_NUMBER() OVER (PARTITION BY CityId ORDER BY COUNT(*) DESC) AS rn
              FROM Bookings
              WHERE {where}
              GROUP BY CityId, ServiceId
            ) AS ranked
            WHERE rn <= 3
            ORDER BY CityId, rn
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new TopServiceDto
        {
            CityId = reader.GetInt32(0),
            ServiceId = reader.GetInt32(1),
            ServiceName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            BookingCount = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            Rank = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture)
        });
    }

    public Task<IReadOnlyList<PeakHourDto>> PeakHoursAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(window, "BookingDate", "CityId", "Status <> 'Cancelled'");
        var sql = $"""
            SET DATEFIRST 7;
            SELECT DATEPART(weekday, BookingDate) AS DayOfWeek,
                   DATEPART(hour, BookingDate) AS HourOfDay,
                   COUNT(*) AS BookingCount
            FROM Bookings
            WHERE {where}
            GROUP BY DATEPART(weekday, BookingDate), DATEPART(hour, BookingDate)
            ORDER BY DayOfWeek, HourOfDay
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new PeakHourDto
        {
            DayOfWeek = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            HourOfDay = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            BookingCount = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)
        });
    }

    public Task<IReadOnlyList<CancellationReportDto>> CancellationsAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(window, "BookingDate", "CityId", null);
        var sql = $"""
            SELECT CityId,
                   COUNT(*) AS TotalBookings,
                   SUM(CASE WHEN Status = N'Cancelled' THEN 1.0 ELSE 0 END) AS CancelledCount,
                   ROUND(100.0 * SUM(CASE WHEN Status = N'Cancelled' THEN 1.0 ELSE 0 END) / COUNT(*), 2) AS CancellationRatePct,
                   AVG(CASE
                         WHEN AcceptedAt IS NOT NULL
                         THEN DATEDIFF(SECOND, CreatedAt, AcceptedAt)
                       END) AS AvgSecondsToAccept
            FROM Bookings
            {where}
            GROUP BY CityId
            ORDER BY CityId
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new CancellationReportDto
        {
            CityId = reader.GetInt32(0),
            TotalBookings = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            CancelledCount = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
            CancellationRatePct = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
            AvgSecondsToAccept = reader.IsDBNull(4) ? null : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture)
        });
    }

    public Task<IReadOnlyList<AddOnRevenueDto>> AddOnRevenueAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(window, "b.CompletedAt", "b.CityId", "b.Status = 'Completed'");
        var sql = $"""
            SELECT ba.AddOnId,
                   ba.NameSnapshot,
                   COUNT(*) AS TimesSold,
                   SUM(ba.PriceSnapshot) AS AddOnRevenue
            FROM BookingAddOns AS ba
            INNER JOIN Bookings AS b ON b.BookingId = ba.BookingId
            WHERE {where}
            GROUP BY ba.AddOnId, ba.NameSnapshot
            ORDER BY AddOnRevenue DESC
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new AddOnRevenueDto
        {
            AddOnId = reader.GetInt32(0),
            Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            TimesSold = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
            AddOnRevenue = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture)
        });
    }

    public Task<IReadOnlyList<WasherPerformanceDto>> WasherPerformanceAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (where, parameters) = Where(
            window,
            "CompletedAt",
            "CityId",
            "Status = 'Completed' AND WasherId IS NOT NULL AND StartedAt IS NOT NULL AND CompletedAt IS NOT NULL");
        var sql = $"""
            SELECT WasherId,
                   COUNT(*) AS CompletedCount,
                   SUM(TotalAmount) AS Revenue,
                   AVG(CAST(DATEDIFF(MINUTE, StartedAt, CompletedAt) AS decimal(10,2))) AS AvgMinutes
            FROM Bookings
            WHERE {where}
            GROUP BY WasherId
            ORDER BY CompletedCount DESC
            """;
        return QueryAsync(sql, parameters, cancellationToken, reader => new WasherPerformanceDto
        {
            WasherId = reader.GetInt32(0),
            CompletedCount = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            Revenue = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
            AvgMinutes = reader.IsDBNull(3) ? null : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture)
        });
    }

    private static (string Where, List<(string Name, object Value)> Parameters) Where(
        ReportWindow window,
        string dateColumn,
        string cityColumn,
        string? required)
    {
        var clauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        if (!string.IsNullOrWhiteSpace(required))
            clauses.Add(required);
        if (window.DateFrom is DateTime from)
        {
            clauses.Add($"{dateColumn} >= @dateFrom");
            parameters.Add(("@dateFrom", ClockUtc.AsUtc(from)));
        }

        if (window.DateTo is DateTime to)
        {
            clauses.Add($"{dateColumn} < @dateTo");
            parameters.Add(("@dateTo", ClockUtc.AsUtc(to)));
        }

        if (window.CityId is int cityId)
        {
            clauses.Add($"{cityColumn} = @cityId");
            parameters.Add(("@cityId", cityId));
        }

        if (clauses.Count == 0)
            return (string.Empty, parameters);

        var joined = string.Join(" AND ", clauses);
        return (required is null ? "WHERE " + joined : joined, parameters);
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken cancellationToken,
        Func<DbDataReader, T> map)
    {
        if (!_db.Database.IsRelational())
            throw new InvalidOperationException("Admin reports run against SQL Server.");

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(map(reader));
        return rows;
    }
}
