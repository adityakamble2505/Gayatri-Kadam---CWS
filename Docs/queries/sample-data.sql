-- Rows used to execute Docs/queries against SQL Server.
-- Re-runnable. Booking rows are replaced. Washer rating rows are inserted once.

USE WasherDB;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.WasherProfiles WHERE UserId = 101)
INSERT INTO dbo.WasherProfiles
    (UserId, CityId, IsAvailable, IsActive, IsQualified, AssignmentStatus, Latitude, Longitude, AvgRating, TotalReviews)
VALUES
    (101, 1, 1, 1, 1, N'Available', 40.758000, -73.985000, 4.50, 2);

IF NOT EXISTS (SELECT 1 FROM dbo.WasherProfiles WHERE UserId = 102)
INSERT INTO dbo.WasherProfiles
    (UserId, CityId, IsAvailable, IsActive, IsQualified, AssignmentStatus, Latitude, Longitude, AvgRating, TotalReviews)
VALUES
    (102, 1, 1, 1, 1, N'Available', 40.678000, -73.944000, 4.90, 10);
GO

USE BookingDB;
GO

DELETE h
FROM dbo.BookingStatusHistory AS h
INNER JOIN dbo.Bookings AS b ON b.BookingId = h.BookingId
WHERE b.BookingNumber LIKE N'GCW-SQL-%';

DELETE a
FROM dbo.BookingAddOns AS a
INNER JOIN dbo.Bookings AS b ON b.BookingId = a.BookingId
WHERE b.BookingNumber LIKE N'GCW-SQL-%';

DELETE o
FROM dbo.BookingOffers AS o
INNER JOIN dbo.Bookings AS b ON b.BookingId = o.BookingId
WHERE b.BookingNumber LIKE N'GCW-SQL-%';

DELETE r
FROM dbo.Reviews AS r
INNER JOIN dbo.Bookings AS b ON b.BookingId = r.BookingId
WHERE b.BookingNumber LIKE N'GCW-SQL-%';

DELETE FROM dbo.OutboxEvents WHERE EventType = N'SamplePoll';
DELETE FROM dbo.Bookings WHERE BookingNumber LIKE N'GCW-SQL-%';
GO

DECLARE @now datetime2(6) = SYSUTCDATETIME();

INSERT INTO dbo.Bookings
    (BookingNumber, CustomerId, VehicleId, ServiceId, WasherId, CityId, BookingType, BookingDate,
     EstimatedDurationMin, Address, Latitude, Longitude, ServiceNameSnapshot, ServicePriceSnapshot,
     TotalAmount, WaterSavedLiters, Status, DispatchStartedAt, AcceptedAt, StartedAt, CompletedAt, CancelledAt, CancelledBy, CreatedAt)
VALUES
    (N'GCW-SQL-Q1', 1, 1, 1, NULL, 1, N'Immediate', @now,
     30, N'10 Main St, New York', 40.712800, -74.006000, N'Basic', 19.99,
     19.99, 15, N'Pending', NULL, NULL, NULL, NULL, NULL, NULL, @now),
    (N'GCW-SQL-Q2', 1, 1, 1, NULL, 1, N'Scheduled', DATEADD(minute, 30, @now),
     30, N'10 Main St, New York', 40.712800, -74.006000, N'Basic', 19.99,
     19.99, 15, N'Scheduled', NULL, NULL, NULL, NULL, NULL, NULL, @now),
    (N'GCW-SQL-Q3', 1, 1, 2, NULL, 1, N'Scheduled', DATEADD(minute, -50, @now),
     45, N'10 Main St, New York', 40.712800, -74.006000, N'Premium', 39.99,
     39.99, 30, N'Scheduled', DATEADD(minute, -40, @now), NULL, NULL, NULL, NULL, NULL, DATEADD(minute, -55, @now)),
    (N'GCW-SQL-Q4', 1, 1, 1, 1, 1, N'Scheduled', @now,
     60, N'10 Main St, New York', 40.712800, -74.006000, N'Basic', 19.99,
     19.99, 15, N'Accepted', DATEADD(minute, -10, @now), @now, NULL, NULL, NULL, NULL, DATEADD(minute, -5, @now)),
    (N'GCW-SQL-Q5C', 1, 1, 1, NULL, 1, N'Immediate', DATEADD(hour, 1, @now),
     30, N'10 Main St, New York', 40.712800, -74.006000, N'Basic', 19.99,
     19.99, 15, N'Pending', NULL, NULL, NULL, NULL, NULL, NULL, @now),
    (N'GCW-SQL-Q5P', 1, 1, 3, 1, 1, N'Scheduled', '2026-11-20T15:00:00',
     75, N'10 Main St, New York', 40.712800, -74.006000, N'Deluxe', 69.99,
     69.99, 50, N'Completed', '2026-11-20T13:00:00', '2026-11-20T14:00:00', '2026-11-20T15:00:00', '2026-11-20T16:00:00', NULL, NULL, '2026-11-20T13:30:00'),
    (N'GCW-SQL-Q6', 1, 1, 1, 1, 1, N'Scheduled', DATEADD(minute, 120, @now),
     30, N'10 Main St, New York', 40.712800, -74.006000, N'Basic', 19.99,
     19.99, 15, N'Accepted', @now, @now, NULL, NULL, NULL, NULL, @now),
    (N'GCW-SQL-Q7A', 2, 2, 1, 1, 2, N'Immediate', '2026-11-18T18:00:00',
     30, N'20 Broadway, Los Angeles', 34.052200, -118.243700, N'Basic', 19.99,
     19.99, 15, N'Completed', '2026-11-18T17:00:00', '2026-11-18T17:05:00', '2026-11-18T17:10:00', '2026-11-18T17:40:00', NULL, NULL, '2026-11-18T16:50:00'),
    (N'GCW-SQL-Q7B', 1, 1, 2, 1, 1, N'Immediate', '2026-11-19T12:00:00',
     45, N'10 Main St, New York', 40.712800, -74.006000, N'Premium', 39.99,
     39.99, 30, N'Completed', '2026-11-19T11:00:00', '2026-11-19T11:10:00', '2026-11-19T11:20:00', '2026-11-19T12:05:00', NULL, NULL, '2026-11-19T11:00:00'),
    (N'GCW-SQL-CXL', 2, 2, 1, NULL, 2, N'Immediate', '2026-11-19T09:00:00',
     30, N'20 Broadway, Los Angeles', 34.052200, -118.243700, N'Basic', 19.99,
     19.99, 0, N'Cancelled', NULL, NULL, NULL, NULL, '2026-11-19T08:30:00', N'Customer', '2026-11-19T08:00:00');

INSERT INTO dbo.BookingAddOns (BookingId, AddOnId, NameSnapshot, PriceSnapshot)
SELECT BookingId, 1, N'Interior Vacuum', 14.99
FROM dbo.Bookings
WHERE BookingNumber = N'GCW-SQL-Q5P';

INSERT INTO dbo.OutboxEvents (EventType, Payload, NextAttemptAt)
VALUES (N'SamplePoll', N'{"bookingId":1}', DATEADD(minute, -1, SYSUTCDATETIME()));
GO

USE NotificationDB;
GO

DELETE FROM dbo.Notifications WHERE Title = N'SQL sample';
INSERT INTO dbo.Notifications (UserId, Type, Title, Message, ReferenceType, ReferenceId, IsRead)
VALUES (1, N'Accepted', N'SQL sample', N'Your wash was accepted.', N'Booking', 1, 0);
GO

USE PaymentDB;
GO

DELETE FROM dbo.Payments WHERE IdempotencyKey = N'sql-sample-idem';
GO
