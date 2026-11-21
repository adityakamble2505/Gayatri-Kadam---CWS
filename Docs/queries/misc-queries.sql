-- Cross-cutting queries (SQL Server). Each section names its own database.
-- No statement joins across databases. All timestamps are UTC.
-- Duplicate-key errors are SQL Server 2601 (unique index) and 2627 (unique constraint).
-- Notification idempotency uses sp_getapplock with @LockOwner = 'Transaction'.
-- The unique index remains the fail-closed guard.

-- -----------------------------------------------------------------------------
-- Q11 Atomic promo redeem (CatalogDB)
-- The application reads @@ROWCOUNT immediately after the UPDATE.
-- -----------------------------------------------------------------------------
USE CatalogDB;
GO

DECLARE @code nvarchar(30) = N'WELCOME10';
DECLARE @subtotal decimal(10,2) = 40.00;

UPDATE dbo.PromoCodes
SET UsedCount = UsedCount + 1
WHERE Code = @code
  AND IsActive = 1
  AND SYSUTCDATETIME() BETWEEN ValidFrom AND ValidTo
  AND @subtotal >= MinOrderAmount
  AND (UsageLimit IS NULL OR UsedCount < UsageLimit);

SELECT @@ROWCOUNT AS RedeemedRowCount, Code, UsedCount
FROM dbo.PromoCodes
WHERE Code = @code;
GO

-- -----------------------------------------------------------------------------
-- Q12 Duplicate payment protection (PaymentDB)
-- A second insert for the same booking or the same idempotency key raises
-- 2601 or 2627. The API maps that to HTTP 409.
-- -----------------------------------------------------------------------------
USE PaymentDB;
GO

DECLARE @bookingId int = 9001;
DECLARE @customerId int = 1;
DECLARE @amount decimal(10,2) = 19.99;
DECLARE @paymentMethod nvarchar(20) = N'Card';
DECLARE @idempotencyKey nvarchar(64) = N'sql-sample-idem';

INSERT INTO dbo.Payments (
    BookingId, CustomerId, Amount, Currency, PaymentMethod,
    PaymentStatus, IdempotencyKey
) VALUES (
    @bookingId, @customerId, @amount, 'USD', @paymentMethod,
    N'Pending', @idempotencyKey
);

SELECT PaymentId, BookingId, CustomerId, Amount, Currency, PaymentMethod,
       PaymentStatus, TransactionReference, IdempotencyKey,
       RefundedAmount, PaymentDate
FROM dbo.Payments
WHERE IdempotencyKey = @idempotencyKey;
GO

-- -----------------------------------------------------------------------------
-- Q13 Outbox poller (BookingDB)
-- One atomic claim. SET RetryCount = RetryCount does not change the value;
-- it is there so the claim is a single UPDATE...OUTPUT.
-- The failure update reads the pre-update RetryCount, so the first wait is
-- POWER(2, RetryCount + 1) minutes. RetryCount 0 waits 2 minutes.
-- The exponent is capped: at RetryCount >= 4 the wait is 30 minutes, and
-- POWER is not called with an exponent that would overflow float.
-- -----------------------------------------------------------------------------
USE BookingDB;
GO

DECLARE @eventId bigint = (
    SELECT TOP (1) EventId
    FROM dbo.OutboxEvents
    WHERE EventType = N'SamplePoll' AND ProcessedAt IS NULL
    ORDER BY EventId
);

WITH cte AS (
    SELECT TOP (20) EventId, EventType, Payload, RetryCount
    FROM dbo.OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE ProcessedAt IS NULL
      AND NextAttemptAt <= SYSUTCDATETIME()
      AND EventId = @eventId
    ORDER BY EventId
)
UPDATE cte
SET RetryCount = RetryCount
OUTPUT inserted.EventId, inserted.EventType, inserted.Payload, inserted.RetryCount;

UPDATE dbo.OutboxEvents
SET RetryCount = RetryCount + 1,
    NextAttemptAt = DATEADD(
        minute,
        CASE
            WHEN RetryCount >= 4 THEN 30
            ELSE CAST(POWER(CAST(2 AS float), RetryCount + 1) AS int)
        END,
        SYSUTCDATETIME()),
    LastError = N'sample failure'
WHERE EventId = @eventId
  AND ProcessedAt IS NULL;

SELECT EventId, RetryCount, DATEDIFF(minute, SYSUTCDATETIME(), NextAttemptAt) AS BackoffMinutes, LastError
FROM dbo.OutboxEvents
WHERE EventId = @eventId;
GO

-- -----------------------------------------------------------------------------
-- Q14 Unread count and mark-all-read (NotificationDB)
-- -----------------------------------------------------------------------------
USE NotificationDB;
GO

DECLARE @userId int = 1;

SELECT COUNT(*) AS UnreadCount
FROM dbo.Notifications
WHERE UserId = @userId
  AND IsRead = 0;

UPDATE dbo.Notifications
SET IsRead = 1,
    ReadAt = SYSUTCDATETIME()
WHERE UserId = @userId
  AND IsRead = 0;

SELECT COUNT(*) AS UnreadCountAfter
FROM dbo.Notifications
WHERE UserId = @userId
  AND IsRead = 0;
GO
