BEGIN;

CREATE TABLE IF NOT EXISTS player_badges
(
    user_id uuid NOT NULL,
    badge_key varchar(40) NOT NULL,
    scope_key varchar(64) NOT NULL DEFAULT 'global',
    event_id uuid,
    unlocked_at timestamp with time zone NOT NULL DEFAULT now(),
    seen_at timestamp with time zone,
    CONSTRAINT pk_player_badges PRIMARY KEY (user_id, badge_key, scope_key),
    CONSTRAINT fk_player_badges_user FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_player_badges_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE CASCADE,
    CONSTRAINT ck_player_badges_key CHECK (badge_key ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    CONSTRAINT ck_player_badges_scope CHECK (btrim(scope_key) <> ''),
    CONSTRAINT ck_player_badges_event_scope CHECK
        ((event_id IS NULL AND scope_key = 'global') OR event_id IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_player_badges_unseen
    ON player_badges (user_id, unlocked_at)
    WHERE seen_at IS NULL;

COMMIT;
