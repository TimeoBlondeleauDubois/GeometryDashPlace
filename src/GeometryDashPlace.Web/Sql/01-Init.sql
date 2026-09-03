BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS users
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    google_subject varchar(255) NOT NULL,
    email varchar(320) NOT NULL,
    display_name varchar(100) NOT NULL,
    avatar_url text,
    is_email_verified boolean NOT NULL DEFAULT false,
    is_admin boolean NOT NULL DEFAULT false,
    is_banned boolean NOT NULL DEFAULT false,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    last_login_at timestamp with time zone,
    CONSTRAINT uq_users_google_subject UNIQUE (google_subject),
    CONSTRAINT ck_users_google_subject_not_blank CHECK (btrim(google_subject) <> ''),
    CONSTRAINT ck_users_email_not_blank CHECK (btrim(email) <> ''),
    CONSTRAINT ck_users_display_name_not_blank CHECK (btrim(display_name) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_email_lower
    ON users (lower(email));

CREATE TABLE IF NOT EXISTS events
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    slug varchar(80) NOT NULL,
    name varchar(120) NOT NULL,
    description text,
    width integer NOT NULL DEFAULT 1024,
    height integer NOT NULL DEFAULT 32,
    cooldown_seconds integer NOT NULL DEFAULT 60,
    status varchar(16) NOT NULL DEFAULT 'draft',
    current_revision bigint NOT NULL DEFAULT 0,
    starts_at timestamp with time zone,
    ends_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT uq_events_slug UNIQUE (slug),
    CONSTRAINT ck_events_slug CHECK (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    CONSTRAINT ck_events_name_not_blank CHECK (btrim(name) <> ''),
    CONSTRAINT ck_events_dimensions CHECK (width > 0 AND height > 0),
    CONSTRAINT ck_events_cooldown CHECK (cooldown_seconds >= 0),
    CONSTRAINT ck_events_status CHECK (status IN ('draft', 'open', 'closed', 'archived')),
    CONSTRAINT ck_events_revision CHECK (current_revision >= 0),
    CONSTRAINT ck_events_dates CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at)
);

CREATE TABLE IF NOT EXISTS object_types
(
    key varchar(64) PRIMARY KEY,
    display_name varchar(100) NOT NULL,
    category varchar(32) NOT NULL,
    geometry_dash_object_id integer,
    y_offset numeric(8, 3) NOT NULL DEFAULT 0,
    rotation_mode varchar(16) NOT NULL DEFAULT 'free',
    can_scale boolean NOT NULL DEFAULT true,
    has_color_settings boolean NOT NULL DEFAULT false,
    has_duration_setting boolean NOT NULL DEFAULT false,
    asset_path text,
    is_active boolean NOT NULL DEFAULT true,
    CONSTRAINT ck_object_types_key CHECK (key ~ '^[a-z0-9]+(?:_[a-z0-9]+)*$'),
    CONSTRAINT ck_object_types_name_not_blank CHECK (btrim(display_name) <> ''),
    CONSTRAINT ck_object_types_category CHECK
        (category IN ('block', 'hazard', 'portal', 'pad', 'orb', 'trigger', 'speed', 'decoration')),
    CONSTRAINT ck_object_types_rotation_mode CHECK
        (rotation_mode IN ('none', 'quarter_turns', 'free')),
    CONSTRAINT ck_object_types_gd_id CHECK
        (geometry_dash_object_id IS NULL OR geometry_dash_object_id > 0)
);

CREATE INDEX IF NOT EXISTS ix_object_types_category
    ON object_types (category, is_active);

CREATE TABLE IF NOT EXISTS user_event_states
(
    event_id uuid NOT NULL,
    user_id uuid NOT NULL,
    placement_count bigint NOT NULL DEFAULT 0,
    last_placement_at timestamp with time zone,
    next_placement_at timestamp with time zone NOT NULL DEFAULT '-infinity',
    CONSTRAINT pk_user_event_states PRIMARY KEY (event_id, user_id),
    CONSTRAINT fk_user_event_states_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_event_states_user FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT ck_user_event_states_count CHECK (placement_count >= 0),
    CONSTRAINT ck_user_event_states_dates CHECK
        (last_placement_at IS NULL OR next_placement_at >= last_placement_at)
);

CREATE INDEX IF NOT EXISTS ix_user_event_states_next_placement
    ON user_event_states (event_id, next_placement_at);

CREATE TABLE IF NOT EXISTS level_cells
(
    event_id uuid NOT NULL,
    x integer NOT NULL,
    y integer NOT NULL,
    object_type_key varchar(64) NOT NULL,
    rotation numeric(7, 3) NOT NULL DEFAULT 0,
    scale_x numeric(6, 3) NOT NULL DEFAULT 1,
    scale_y numeric(6, 3) NOT NULL DEFAULT 1,
    color_red smallint,
    color_green smallint,
    color_blue smallint,
    duration_seconds numeric(8, 3),
    author_user_id uuid NOT NULL,
    placed_at timestamp with time zone NOT NULL DEFAULT now(),
    revision bigint NOT NULL,
    CONSTRAINT pk_level_cells PRIMARY KEY (event_id, x, y),
    CONSTRAINT fk_level_cells_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE CASCADE,
    CONSTRAINT fk_level_cells_object_type FOREIGN KEY (object_type_key)
        REFERENCES object_types (key),
    CONSTRAINT fk_level_cells_author FOREIGN KEY (author_user_id)
        REFERENCES users (id),
    CONSTRAINT ck_level_cells_coordinates CHECK (x >= 0 AND y >= 0),
    CONSTRAINT ck_level_cells_rotation CHECK (rotation >= 0 AND rotation < 360),
    CONSTRAINT ck_level_cells_scale_x CHECK (scale_x BETWEEN 0.5 AND 2),
    CONSTRAINT ck_level_cells_scale_y CHECK (scale_y BETWEEN 0.5 AND 2),
    CONSTRAINT ck_level_cells_red CHECK (color_red IS NULL OR color_red BETWEEN 0 AND 255),
    CONSTRAINT ck_level_cells_green CHECK (color_green IS NULL OR color_green BETWEEN 0 AND 255),
    CONSTRAINT ck_level_cells_blue CHECK (color_blue IS NULL OR color_blue BETWEEN 0 AND 255),
    CONSTRAINT ck_level_cells_complete_color CHECK
        ((color_red IS NULL AND color_green IS NULL AND color_blue IS NULL)
        OR (color_red IS NOT NULL AND color_green IS NOT NULL AND color_blue IS NOT NULL)),
    CONSTRAINT ck_level_cells_duration CHECK (duration_seconds IS NULL OR duration_seconds >= 0),
    CONSTRAINT ck_level_cells_revision CHECK (revision > 0)
);

CREATE INDEX IF NOT EXISTS ix_level_cells_author
    ON level_cells (author_user_id);

CREATE INDEX IF NOT EXISTS ix_level_cells_revision
    ON level_cells (event_id, revision);

CREATE TABLE IF NOT EXISTS placement_history
(
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id uuid NOT NULL,
    revision bigint NOT NULL,
    request_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    x integer NOT NULL,
    y integer NOT NULL,
    action varchar(16) NOT NULL,
    previous_object jsonb,
    new_object jsonb,
    placed_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT fk_placement_history_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE CASCADE,
    CONSTRAINT fk_placement_history_user FOREIGN KEY (user_id)
        REFERENCES users (id),
    CONSTRAINT uq_placement_history_event_revision UNIQUE (event_id, revision),
    CONSTRAINT uq_placement_history_request UNIQUE (request_id),
    CONSTRAINT ck_placement_history_revision CHECK (revision > 0),
    CONSTRAINT ck_placement_history_coordinates CHECK (x >= 0 AND y >= 0),
    CONSTRAINT ck_placement_history_action CHECK (action IN ('place', 'replace', 'delete')),
    CONSTRAINT ck_placement_history_objects CHECK
    (
        (action = 'place' AND previous_object IS NULL AND new_object IS NOT NULL)
        OR (action = 'replace' AND previous_object IS NOT NULL AND new_object IS NOT NULL)
        OR (action = 'delete' AND previous_object IS NOT NULL AND new_object IS NULL)
    ),
    CONSTRAINT ck_placement_history_previous_json CHECK
        (previous_object IS NULL OR jsonb_typeof(previous_object) = 'object'),
    CONSTRAINT ck_placement_history_new_json CHECK
        (new_object IS NULL OR jsonb_typeof(new_object) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_placement_history_event_date
    ON placement_history (event_id, placed_at);

CREATE INDEX IF NOT EXISTS ix_placement_history_user_date
    ON placement_history (user_id, placed_at DESC);

CREATE INDEX IF NOT EXISTS ix_placement_history_cell
    ON placement_history (event_id, x, y, revision);

CREATE TABLE IF NOT EXISTS level_snapshots
(
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id uuid NOT NULL,
    revision bigint NOT NULL,
    snapshot_type varchar(16) NOT NULL DEFAULT 'hourly',
    state jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT fk_level_snapshots_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE CASCADE,
    CONSTRAINT uq_level_snapshots_event_revision UNIQUE (event_id, revision),
    CONSTRAINT ck_level_snapshots_revision CHECK (revision >= 0),
    CONSTRAINT ck_level_snapshots_type CHECK (snapshot_type IN ('hourly', 'manual', 'final')),
    CONSTRAINT ck_level_snapshots_state CHECK (jsonb_typeof(state) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_level_snapshots_event_date
    ON level_snapshots (event_id, created_at DESC);

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_events_set_updated_at ON events;
CREATE TRIGGER trg_events_set_updated_at
    BEFORE UPDATE ON events
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

CREATE OR REPLACE FUNCTION validate_level_cell()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    event_width integer;
    event_height integer;
    object_has_color boolean;
    object_has_duration boolean;
    object_rotation_mode varchar(16);
    object_can_scale boolean;
BEGIN
    SELECT width, height
    INTO event_width, event_height
    FROM events
    WHERE id = NEW.event_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Unknown event: %', NEW.event_id;
    END IF;

    IF NEW.x >= event_width OR NEW.y >= event_height THEN
        RAISE EXCEPTION 'Cell (%, %) is outside the event grid (% x %)',
            NEW.x, NEW.y, event_width, event_height;
    END IF;

    SELECT has_color_settings, has_duration_setting, rotation_mode, can_scale
    INTO object_has_color, object_has_duration, object_rotation_mode, object_can_scale
    FROM object_types
    WHERE key = NEW.object_type_key;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Unknown object type: %', NEW.object_type_key;
    END IF;

    IF object_has_color AND
       (NEW.color_red IS NULL OR NEW.color_green IS NULL OR NEW.color_blue IS NULL) THEN
        RAISE EXCEPTION 'Object type % requires an RGB color', NEW.object_type_key;
    END IF;

    IF NOT object_has_color AND
       (NEW.color_red IS NOT NULL OR NEW.color_green IS NOT NULL OR NEW.color_blue IS NOT NULL) THEN
        RAISE EXCEPTION 'Object type % does not accept an RGB color', NEW.object_type_key;
    END IF;

    IF object_has_duration AND NEW.duration_seconds IS NULL THEN
        RAISE EXCEPTION 'Object type % requires a duration', NEW.object_type_key;
    END IF;

    IF NOT object_has_duration AND NEW.duration_seconds IS NOT NULL THEN
        RAISE EXCEPTION 'Object type % does not accept a duration', NEW.object_type_key;
    END IF;

    IF object_rotation_mode = 'none' AND NEW.rotation <> 0 THEN
        RAISE EXCEPTION 'Object type % does not accept rotation', NEW.object_type_key;
    END IF;

    IF object_rotation_mode = 'quarter_turns' AND mod(NEW.rotation, 90) <> 0 THEN
        RAISE EXCEPTION 'Object type % only accepts quarter-turn rotation', NEW.object_type_key;
    END IF;

    IF NOT object_can_scale AND (NEW.scale_x <> 1 OR NEW.scale_y <> 1) THEN
        RAISE EXCEPTION 'Object type % does not accept scaling', NEW.object_type_key;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_level_cells_validate ON level_cells;
CREATE TRIGGER trg_level_cells_validate
    BEFORE INSERT OR UPDATE ON level_cells
    FOR EACH ROW
    EXECUTE FUNCTION validate_level_cell();

CREATE OR REPLACE VIEW level_export AS
SELECT
    cell.event_id,
    cell.x,
    cell.y,
    cell.object_type_key AS type,
    object_type.geometry_dash_object_id,
    object_type.y_offset,
    cell.rotation,
    cell.scale_x,
    cell.scale_y,
    cell.color_red AS red,
    cell.color_green AS green,
    cell.color_blue AS blue,
    cell.duration_seconds AS duration,
    user_account.display_name AS author,
    cell.revision,
    cell.placed_at
FROM level_cells AS cell
JOIN object_types AS object_type ON object_type.key = cell.object_type_key
JOIN users AS user_account ON user_account.id = cell.author_user_id;

COMMIT;
