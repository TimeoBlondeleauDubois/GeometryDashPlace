BEGIN;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS username varchar(20),
    ADD COLUMN IF NOT EXISTS normalized_username varchar(20),
    ADD COLUMN IF NOT EXISTS avatar_png bytea,
    ADD COLUMN IF NOT EXISTS google_avatar_url text,
    ADD COLUMN IF NOT EXISTS is_profile_completed boolean NOT NULL DEFAULT false;

UPDATE users
SET google_avatar_url = avatar_url
WHERE google_avatar_url IS NULL
  AND avatar_url IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_normalized_username
    ON users (normalized_username)
    WHERE normalized_username IS NOT NULL;

COMMIT;
