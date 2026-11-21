-- One-time cleanup. BookingDB outbox events 1-10 were written before the
-- standard payload existed, so notification intake rejects them with 400.
-- Keep the rows and stop the publisher from retrying them.
USE BookingDB;

UPDATE OutboxEvents
SET ProcessedAt = NOW(6),
    LastError = 'Legacy payload shape, discarded'
WHERE EventId BETWEEN 1 AND 10
  AND ProcessedAt IS NULL;
