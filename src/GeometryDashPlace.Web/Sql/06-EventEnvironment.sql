BEGIN;

ALTER TABLE events
    ADD COLUMN IF NOT EXISTS background_key varchar(64) NOT NULL DEFAULT 'background-01';

ALTER TABLE events
    ADD COLUMN IF NOT EXISTS ground_key varchar(64) NOT NULL DEFAULT 'ground-01';

COMMIT;
