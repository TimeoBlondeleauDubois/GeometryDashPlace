BEGIN;

ALTER TABLE events
    ADD COLUMN IF NOT EXISTS last_snapshot_at timestamp with time zone;

COMMIT;
