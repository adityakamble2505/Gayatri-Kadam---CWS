-- BookingDB admin reports (SQL Server).
-- DATEFIRST 7 is the SQL Server default: DATEPART(weekday) returns 1 for Sunday
-- through 7 for Saturday.
-- All timestamps are UTC.

USE BookingDB;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R1 Filtered order report
-- Optional predicates are appended only when the caller passed a value.
-- -----------------------------------------------------------------------------
DECLARE @skip int = 0;
DECLARE @take int = 50;
DECLARE @cityId int = 1;
DECLARE @status nvarchar(20) = N'Completed';
DECLARE @fromUtc datetime2(6) = '2026-11-01T00:00:00';
DECLARE @toUtc datetime2(6) = '2026-12-01T00:00:00';

SELECT BookingId, BookingNumber, CustomerId, VehicleId, ServiceId, WasherId,
       CityId, BookingType, BookingDate, Status, TotalAmount,
       CreatedAt, CompletedAt, CancelledAt
FROM dbo.Bookings
WHERE CityId = @cityId
  AND Status = @status
  AND BookingDate >= @fromUtc
  AND BookingDate < @toUtc
ORDER BY CreatedAt DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R2 Daily revenue with a running total
-- -----------------------------------------------------------------------------
SELECT CAST(CompletedAt AS date) AS Day,
       COUNT(*) AS CompletedCount,
       SUM(TotalAmount) AS Revenue,
       SUM(SUM(TotalAmount)) OVER (ORDER BY CAST(CompletedAt AS date)) AS RunningTotal
FROM dbo.Bookings
WHERE Status = N'Completed'
  AND CompletedAt IS NOT NULL
GROUP BY CAST(CompletedAt AS date)
ORDER BY Day;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R3 Top 3 services per city
-- -----------------------------------------------------------------------------
SELECT CityId, ServiceId, ServiceName, BookingCount
FROM (
    SELECT CityId,
           ServiceId,
           MAX(ServiceNameSnapshot) AS ServiceName,
           COUNT(*) AS BookingCount,
           ROW_NUMBER() OVER (PARTITION BY CityId ORDER BY COUNT(*) DESC) AS rn
    FROM dbo.Bookings
    WHERE Status <> N'Cancelled'
    GROUP BY CityId, ServiceId
) AS ranked
WHERE rn <= 3
ORDER BY CityId, rn;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R4 Peak hours heatmap
-- DATEPART(weekday) with DATEFIRST 7: 1 = Sunday ... 7 = Saturday.
-- -----------------------------------------------------------------------------
SELECT DATEPART(weekday, BookingDate) AS DayOfWeek,
       DATEPART(hour, BookingDate) AS HourOfDay,
       COUNT(*) AS BookingCount
FROM dbo.Bookings
WHERE Status <> N'Cancelled'
GROUP BY DATEPART(weekday, BookingDate), DATEPART(hour, BookingDate)
ORDER BY DayOfWeek, HourOfDay;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R5 Cancellation rate and average seconds to accept, per city
-- SUM(Status = 'Cancelled') is not valid T-SQL. The CASE expression is the count.
-- -----------------------------------------------------------------------------
SELECT CityId,
       COUNT(*) AS TotalBookings,
       SUM(CASE WHEN Status = N'Cancelled' THEN 1.0 ELSE 0 END) AS CancelledCount,
       ROUND(100.0 * SUM(CASE WHEN Status = N'Cancelled' THEN 1.0 ELSE 0 END) / COUNT(*), 2) AS CancellationRatePct,
       AVG(CASE
             WHEN AcceptedAt IS NOT NULL
             THEN DATEDIFF(SECOND, CreatedAt, AcceptedAt)
           END) AS AvgSecondsToAccept
FROM dbo.Bookings
GROUP BY CityId
ORDER BY CityId;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R6 Add-on revenue
-- -----------------------------------------------------------------------------
SELECT ba.AddOnId,
       ba.NameSnapshot,
       COUNT(*) AS TimesSold,
       SUM(ba.PriceSnapshot) AS AddOnRevenue
FROM dbo.BookingAddOns AS ba
INNER JOIN dbo.Bookings AS b ON b.BookingId = ba.BookingId
WHERE b.Status = N'Completed'
GROUP BY ba.AddOnId, ba.NameSnapshot
ORDER BY AddOnRevenue DESC;
GO

SET DATEFIRST 7;

-- -----------------------------------------------------------------------------
-- R7 Washer performance
-- DATEDIFF(MINUTE) counts minute boundaries between StartedAt and CompletedAt.
-- -----------------------------------------------------------------------------
SELECT WasherId,
       COUNT(*) AS CompletedCount,
       SUM(TotalAmount) AS Revenue,
       AVG(DATEDIFF(MINUTE, StartedAt, CompletedAt)) AS AvgMinutes
FROM dbo.Bookings
WHERE Status = N'Completed'
  AND WasherId IS NOT NULL
  AND StartedAt IS NOT NULL
  AND CompletedAt IS NOT NULL
GROUP BY WasherId
ORDER BY CompletedCount DESC;
GO
