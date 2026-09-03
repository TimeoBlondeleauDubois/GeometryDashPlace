BEGIN;

ALTER TABLE placement_history
    ADD COLUMN IF NOT EXISTS source_x integer,
    ADD COLUMN IF NOT EXISTS source_y integer,
    ADD COLUMN IF NOT EXISTS replaced_object jsonb;

ALTER TABLE placement_history
    DROP CONSTRAINT IF EXISTS ck_placement_history_action,
    DROP CONSTRAINT IF EXISTS ck_placement_history_objects,
    DROP CONSTRAINT IF EXISTS ck_placement_history_source_coordinates,
    DROP CONSTRAINT IF EXISTS ck_placement_history_replaced_json;

ALTER TABLE placement_history
    ADD CONSTRAINT ck_placement_history_action CHECK
        (action IN ('place', 'replace', 'delete', 'move', 'move_replace')),
    ADD CONSTRAINT ck_placement_history_source_coordinates CHECK
    (
        (source_x IS NULL AND source_y IS NULL)
        OR (source_x IS NOT NULL AND source_x >= 0 AND source_y IS NOT NULL AND source_y >= 0
            AND (source_x <> x OR source_y <> y))
    ),
    ADD CONSTRAINT ck_placement_history_objects CHECK
    (
        (action = 'place' AND source_x IS NULL AND previous_object IS NULL
            AND new_object IS NOT NULL AND replaced_object IS NULL)
        OR (action = 'replace' AND source_x IS NULL AND previous_object IS NOT NULL
            AND new_object IS NOT NULL AND replaced_object IS NULL)
        OR (action = 'delete' AND source_x IS NULL AND previous_object IS NOT NULL
            AND new_object IS NULL AND replaced_object IS NULL)
        OR (action = 'move' AND source_x IS NOT NULL AND previous_object IS NOT NULL
            AND new_object IS NOT NULL AND replaced_object IS NULL)
        OR (action = 'move_replace' AND source_x IS NOT NULL AND previous_object IS NOT NULL
            AND new_object IS NOT NULL AND replaced_object IS NOT NULL)
    ),
    ADD CONSTRAINT ck_placement_history_replaced_json CHECK
        (replaced_object IS NULL OR jsonb_typeof(replaced_object) = 'object');

COMMIT;
