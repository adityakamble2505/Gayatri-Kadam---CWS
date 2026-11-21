# Index suggestions

These statements cover access paths used by Q1–Q7 and the admin reports. Phase 7 added them to `Docs/schema.sql` and `Docs/migrations/001_phase7.sql`.

Already supported, so not repeated here:

- Q1 accept seeks `Bookings` by primary key `BookingId`.
- Q2 pickup and Q3 escalation use `IX_Booking_Dispatch (Status, WasherId, DispatchStartedAt, BookingDate)`.
- Q4 overlap uses `IX_Booking_Washer (WasherId, Status, BookingDate)`.
- Q13 outbox claim uses `IX_Outbox_Pending (ProcessedAt, NextAttemptAt, EventId)`.
- Review insert uses `UX_Review_Booking_Role`. The washer review list uses `IX_Review_Washer (WasherId, ReviewerRole, CreatedAt)`.
- R2 daily revenue filters `Status` and `CompletedAt` with `IX_Booking_Completed`.
- R6 joins `BookingAddOns` on `BookingId`, the leading column of `UX_BA`.
- Admin order number uses `UX_Booking_Number`. Washer, service, and city filters use `IX_Booking_Washer`, `IX_Booking_Service`, and `IX_Booking_City_Status`.
- `DATEPART` and `CAST(... AS date)` expressions cannot be served by a plain B-tree. A range predicate on the underlying column can.

```sql
-- Q5 lists a customer's current or past orders by status and booking date.
-- IX_Booking_Customer is (CustomerId, CreatedAt), which does not match that filter or order.
CREATE INDEX IX_Booking_Customer_Status_Date ON Bookings (CustomerId, Status, BookingDate);

-- Q6 reminders select Accepted scheduled bookings inside a BookingDate window.
CREATE INDEX IX_Booking_Reminder ON Bookings (Status, BookingType, BookingDate);

-- Q7 ranks completed bookings by customer. IX_Booking_Completed does not include CustomerId.
CREATE INDEX IX_Booking_Leaderboard ON Bookings (Status, CustomerId, WaterSavedLiters);

-- Admin orders and R4 filter status plus a BookingDate range when no city is supplied.
-- IX_Booking_Dispatch starts with Status but then WasherId, so a date range cannot use it.
CREATE INDEX IX_Booking_Status_Date ON Bookings (Status, BookingDate);

-- R2 with a city filter. IX_Booking_Completed is (Status, CompletedAt) and then filters CityId.
CREATE INDEX IX_Booking_Completed_City ON Bookings (Status, CityId, CompletedAt);

-- R7 groups completed bookings by washer and averages StartedAt to CompletedAt.
CREATE INDEX IX_Booking_Washer_Performance ON Bookings (Status, WasherId, StartedAt, CompletedAt);

-- Phase 7. Notification intake serializes duplicates with sp_getapplock.
-- This unique index is the fail-closed guard when that lock times out.
-- Column order matches the intake key (Type, ReferenceType, ReferenceId, UserId).
CREATE UNIQUE INDEX UX_Notif_Idempotency ON Notifications (Type, ReferenceType, ReferenceId, UserId);
```
