BEGIN;

DROP VIEW IF EXISTS level_export;

ALTER TABLE object_types
    ALTER COLUMN geometry_dash_object_id DROP NOT NULL;

ALTER TABLE object_types
    ADD COLUMN IF NOT EXISTS rotation_mode varchar(16) NOT NULL DEFAULT 'free',
    ADD COLUMN IF NOT EXISTS can_scale boolean NOT NULL DEFAULT true;

ALTER TABLE object_types
    DROP CONSTRAINT IF EXISTS ck_object_types_gd_id,
    DROP CONSTRAINT IF EXISTS ck_object_types_rotation_mode;

ALTER TABLE object_types
    ADD CONSTRAINT ck_object_types_gd_id CHECK
        (geometry_dash_object_id IS NULL OR geometry_dash_object_id > 0),
    ADD CONSTRAINT ck_object_types_rotation_mode CHECK
        (rotation_mode IN ('none', 'quarter_turns', 'free'));

ALTER TABLE level_cells
    DROP CONSTRAINT IF EXISTS ck_level_cells_rotation;

ALTER TABLE level_cells
    ALTER COLUMN rotation TYPE numeric(7, 3) USING rotation::numeric;

ALTER TABLE level_cells
    ADD COLUMN IF NOT EXISTS scale_x numeric(6, 3) NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS scale_y numeric(6, 3) NOT NULL DEFAULT 1;

ALTER TABLE level_cells
    DROP CONSTRAINT IF EXISTS ck_level_cells_scale_x,
    DROP CONSTRAINT IF EXISTS ck_level_cells_scale_y;

ALTER TABLE level_cells
    ADD CONSTRAINT ck_level_cells_rotation CHECK (rotation >= 0 AND rotation < 360),
    ADD CONSTRAINT ck_level_cells_scale_x CHECK (scale_x BETWEEN 0.5 AND 2),
    ADD CONSTRAINT ck_level_cells_scale_y CHECK (scale_y BETWEEN 0.5 AND 2);

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
