-- Additive indexes for an already-running GreenCarWash database.
-- Safe to run more than once. Does not drop or rewrite existing rows
-- except duplicate Notifications, which the unique index cannot accept.

USE BookingDB;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Customer_Status_Date') = 0,
  'CREATE INDEX IX_Booking_Customer_Status_Date ON Bookings (CustomerId, Status, BookingDate)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Reminder') = 0,
  'CREATE INDEX IX_Booking_Reminder ON Bookings (Status, BookingType, BookingDate)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Leaderboard') = 0,
  'CREATE INDEX IX_Booking_Leaderboard ON Bookings (Status, CustomerId, WaterSavedLiters)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Status_Date') = 0,
  'CREATE INDEX IX_Booking_Status_Date ON Bookings (Status, BookingDate)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Completed_City') = 0,
  'CREATE INDEX IX_Booking_Completed_City ON Bookings (Status, CityId, CompletedAt)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'BookingDB' AND table_name = 'Bookings' AND index_name = 'IX_Booking_Washer_Performance') = 0,
  'CREATE INDEX IX_Booking_Washer_Performance ON Bookings (Status, WasherId, StartedAt, CompletedAt)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

USE NotificationDB;

DELETE newer
FROM Notifications newer
INNER JOIN Notifications older
  ON newer.Type = older.Type
 AND newer.ReferenceType <=> older.ReferenceType
 AND newer.ReferenceId <=> older.ReferenceId
 AND newer.UserId = older.UserId
 AND newer.NotificationId > older.NotificationId;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = 'NotificationDB' AND table_name = 'Notifications' AND index_name = 'UX_Notif_Idempotency') = 0,
  'CREATE UNIQUE INDEX UX_Notif_Idempotency ON Notifications (Type, ReferenceType, ReferenceId, UserId)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
