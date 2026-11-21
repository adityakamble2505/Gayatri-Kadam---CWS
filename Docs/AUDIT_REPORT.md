# Phase 7 audit

Two steps. The full suite passed after each one: 124 tests after the schema step, 129 tests after the outbox step (0 failed). `Docs/schema.sql` was changed only by adding indexes.

## Fixed

### Indexes were missing for the order lists and reports

`Docs/INDEX_SUGGESTIONS.md` described six BookingDB indexes (customer status/date, reminders, leaderboard, status/date, completed-by-city, washer performance) that were not in the schema. Fresh databases now create them, and `Docs/migrations/001_phase7.sql` adds them on a database that is already running. The script was applied on the running database. `information_schema` shows all six indexes.

### Notification intake could insert a duplicate if the lock timed out

`sp_getapplock` is taken before the check-then-insert. There was no unique key behind it. `UX_Notif_Idempotency` is now on `Notifications (Type, ReferenceType, ReferenceId, UserId)`. A duplicate-key error (SQL Server 2601 or 2627) rolls the insert back and returns "already exists" without failing the request. The migration deletes any older duplicate groups before creating the index. This database had none.

### System actions stored a bare 0 or a null user id

`GreenCarWash.BuildingBlocks.SystemActor.SystemUserId` is 0. Auto-refunds write that value as `ProcessedByUserId`. System history rows (washer confirm/start/complete rollback, and a system cancel) write it as `ChangedByUserId`.

### Booking outbox events 1–10 could never be delivered

Those rows use the old payload shape, so intake returns 400. `Docs/migrations/002_outbox_cleanup.sql` sets `ProcessedAt` and `LastError = 'Legacy payload shape, discarded'`. The rows are still there. Events 11 and later were left alone. The script was applied.

### A 400 from the receiver retried forever

The publisher now treats HTTP 400 as a poison message. The first two attempts keep the row pending with backoff. The third sets `ProcessedAt` and `LastError`. Other failures, including a 5xx and a connection error, still retry. A connection error still stops the rest of the batch. Covered by `Outbox_Http400_IsDiscardedAfterThreeAttempts`.

### Rating sync was acknowledged and then forgotten

`RatingSyncPending` was posted to NotificationService, which returns 200 and stores nothing. The publisher then marked the row processed, so a failed washer rating was never retried. The booking publisher now leaves `RatingSyncPending` and `RefundPending` unclaimed. `RatingSyncRetryWorker` claims `RatingSyncPending` with `FOR UPDATE SKIP LOCKED`, reads the review, and calls `PUT /api/washer/internal/rating`. The payload records `reviewId` when the event is written and `appliedReviewId` after a successful call. A later event for a review that was already applied does not call the washer again. A missing or non-customer review is marked processed so it does not retry. Covered by `RatingSync_FailsThenSucceeds_UpdatesAverageOnce`.

### A failed cancel refund was only an admin alert

`RefundPending` is no longer delivered as a notification. `RefundRetryWorker` claims those rows and calls the same payment refund used at cancel time. Success, or nothing left to refund, sets `ProcessedAt`. A failed call uses the outbox backoff and leaves the row pending. The cancel path still writes one `RefundPending` row when the first refund call fails. Covered by `RefundRetry_FailsThenSucceeds_AndDoesNotRefundTwice` and `RefundRetry_WhenPaymentStaysDown_BacksOffWithoutDiscarding`.

## Left

- A rating call that succeeds inside WasherService, followed by a crash before the booking outbox row is saved, can apply that rating again on the next attempt. The `appliedReviewId` guard runs only after the save. Washer rating itself is still an increment, not an idempotent set.
- Refund retries are not capped. A payment service that stays down is retried on the outbox backoff, topping out at 30 minutes, until it succeeds.
- `EmailLogs` has no body column. Rendered HTML stays in memory until send, and a restart marks those queued rows failed with "Email content is no longer available."
- The washer queue is one in-memory list per process. `Docs/QUEUE_DESIGN.md` already says a second instance needs another implementation of the same interface. `BookingOffers` stays an audit log.
- Unique indexes treat NULL as distinct, so two notifications with a null `ReferenceType` or `ReferenceId` are not rejected by `UX_Notif_Idempotency`. The event mapper sets both.
- PaymentDB outbox event 1 was still inside an earlier timeout backoff when the APIs were stopped. It was not rewritten.
- `Docs/PHASES.md` is still only the phase headings.
- This pass covered the schema, system user id, and the outbox workers in the request. It did not re-audit every endpoint for authorization or logging.
