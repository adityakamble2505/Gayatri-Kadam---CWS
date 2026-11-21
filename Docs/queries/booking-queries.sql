-- BookingDB queries (SQL Server).
-- No cross-database joins. Parameters are T-SQL variables bound by the API.
-- All timestamps are UTC. Run Docs/queries/sample-data.sql first to execute this file.

USE BookingDB;
GO

-- -----------------------------------------------------------------------------
-- Q1 Race-safe accept
-- Two washers can call accept at the same time. Exactly one UPDATE may match.
-- The application reads @@ROWCOUNT immediately after the UPDATE:
--   1 -> write history + outbox in this same transaction, then COMMIT
--   0 -> ROLLBACK and return HTTP 409 Conflict
-- -----------------------------------------------------------------------------
DECLARE @id int = (SELECT BookingId FROM dbo.Bookings WHERE BookingNumber = N'GCW-SQL-Q1');
DECLARE @washerId int = 1;
DECLARE @fromStatus nvarchar(20) = N'Pending';
DECLARE @changedByUserId int = 4;

BEGIN TRANSACTION;

UPDATE dbo.Bookings
SET WasherId   = @washerId,
    Status     = N'Accepted',
    AcceptedAt = SYSUTCDATETIME(),
    UpdatedAt  = SYSUTCDATETIME(),
    RowVersion = RowVersion + 1
WHERE BookingId = @id
  AND WasherId IS NULL
  AND Status IN (N'Pending', N'Scheduled');

DECLARE @accepted int = @@ROWCOUNT;

IF @accepted = 1
BEGIN
    INSERT INTO dbo.BookingStatusHistory
        (BookingId, FromStatus, ToStatus, ChangedByUserId, ChangedByRole, Note)
    VALUES
        (@id, @fromStatus, N'Accepted', @changedByUserId, N'Washer', N'Washer accepted');

    INSERT INTO dbo.OutboxEvents (EventType, Payload)
    VALUES (
        N'BookingAccepted',
        (SELECT @id AS bookingId, @washerId AS washerId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    );
END

COMMIT;

SELECT @accepted AS AcceptedRowCount;
GO

-- -----------------------------------------------------------------------------
-- Q2 Scheduled worker pickup
-- One atomic claim. READPAST lets a second worker take a different batch.
-- Every assignment in the UPDATE reads the pre-update row.
-- -----------------------------------------------------------------------------
DECLARE @claimedAt datetime2(6) = SYSUTCDATETIME();

WITH cte AS (
    SELECT TOP (50)
        BookingId, CityId, BookingDate, Latitude, Longitude, DispatchStartedAt, UpdatedAt
    FROM dbo.Bookings WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = N'Scheduled'
      AND WasherId IS NULL
      AND DispatchStartedAt IS NULL
      AND BookingDate <= DATEADD(hour, 2, @claimedAt)
    ORDER BY BookingDate
)
UPDATE cte
SET DispatchStartedAt = @claimedAt,
    UpdatedAt = @claimedAt
OUTPUT inserted.BookingId, inserted.CityId, inserted.BookingDate, inserted.Latitude, inserted.Longitude;
GO

-- -----------------------------------------------------------------------------
-- Q3 30-minute escalation
-- -----------------------------------------------------------------------------
DECLARE @id int = (SELECT BookingId FROM dbo.Bookings WHERE BookingNumber = N'GCW-SQL-Q3');
DECLARE @washerId int = 1;

UPDATE dbo.Bookings
SET NeedsAdminAttention = 1,
    UpdatedAt = SYSUTCDATETIME(),
    RowVersion = RowVersion + 1
WHERE Status = N'Scheduled'
  AND WasherId IS NULL
  AND DispatchStartedAt <= DATEADD(minute, -30, SYSUTCDATETIME());

UPDATE dbo.Bookings
SET WasherId            = @washerId,
    Status              = N'Accepted',
    AcceptedAt          = SYSUTCDATETIME(),
    NeedsAdminAttention = 1,
    UpdatedAt           = SYSUTCDATETIME(),
    RowVersion          = RowVersion + 1
WHERE BookingId = @id
  AND WasherId IS NULL
  AND Status = N'Scheduled'
  AND DispatchStartedAt <= DATEADD(minute, -30, SYSUTCDATETIME());

SELECT BookingNumber, Status, WasherId, NeedsAdminAttention
FROM dbo.Bookings
WHERE BookingNumber = N'GCW-SQL-Q3';
GO

-- -----------------------------------------------------------------------------
-- Q4 Washer double-booking overlap check
-- Existing window is [BookingDate, BookingDate + EstimatedDurationMin + 30 minutes).
-- -----------------------------------------------------------------------------
DECLARE @washerId int = 1;
DECLARE @newStart datetime2(6) = SYSUTCDATETIME();
DECLARE @newEnd datetime2(6) = DATEADD(minute, 60, @newStart);

SELECT COUNT(*) AS OverlapCount
FROM dbo.Bookings
WHERE WasherId = @washerId
  AND Status IN (N'Accepted', N'InProgress')
  AND BookingDate < @newEnd
  AND DATEADD(minute, EstimatedDurationMin + 30, BookingDate) > @newStart;
GO

-- -----------------------------------------------------------------------------
-- Q5 Customer current orders and past orders
-- -----------------------------------------------------------------------------
DECLARE @customerId int = 1;
DECLARE @skip int = 0;
DECLARE @take int = 20;

SELECT BookingId, BookingNumber, ServiceNameSnapshot, BookingType, BookingDate,
       Status, TotalAmount, WasherId, CityId
FROM dbo.Bookings
WHERE CustomerId = @customerId
  AND Status IN (N'Pending', N'Scheduled', N'Accepted', N'InProgress')
ORDER BY BookingDate DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

SELECT BookingId, BookingNumber, ServiceNameSnapshot, BookingType, BookingDate,
       Status, TotalAmount, WasherId, CityId, CompletedAt, CancelledAt
FROM dbo.Bookings
WHERE CustomerId = @customerId
  AND Status IN (N'Completed', N'Cancelled')
ORDER BY BookingDate DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
GO

-- -----------------------------------------------------------------------------
-- Q6 Washer 2-hour reminder
-- -----------------------------------------------------------------------------
SELECT BookingId, CustomerId, WasherId, CityId, BookingDate, BookingNumber
FROM dbo.Bookings
WHERE Status = N'Accepted'
  AND BookingType = N'Scheduled'
  AND BookingDate BETWEEN DATEADD(minute, 115, SYSUTCDATETIME())
                      AND DATEADD(minute, 125, SYSUTCDATETIME());
GO

-- -----------------------------------------------------------------------------
-- Q7 Leaderboard
-- -----------------------------------------------------------------------------
DECLARE @skip int = 0;
DECLARE @take int = 10;

SELECT CustomerId,
       SUM(WaterSavedLiters) AS Liters,
       COUNT(*) AS Washes,
       DENSE_RANK() OVER (ORDER BY SUM(WaterSavedLiters) DESC) AS Rnk
FROM dbo.Bookings
WHERE Status = N'Completed'
GROUP BY CustomerId
ORDER BY Rnk
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
GO
