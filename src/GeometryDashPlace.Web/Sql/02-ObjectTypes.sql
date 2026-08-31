BEGIN;

INSERT INTO object_types
    (key, display_name, category, geometry_dash_object_id, y_offset,
     has_color_settings, has_duration_setting, asset_path)
VALUES
    ('block', 'Block', 'block', 1, 0, false, false, '/assets/objects/blocks/block.png'),
    ('line_block', 'Line block', 'block', 2, 0, false, false, '/assets/objects/blocks/block-line.png'),
    ('corner_block', 'Corner block', 'block', 3, 0, false, false, '/assets/objects/blocks/block-corner.png'),
    ('dot_block', 'Dot block', 'block', 4, 0, false, false, '/assets/objects/blocks/block-dot.png'),
    ('deco_block', 'Decoration block', 'decoration', 5, 0, false, false, '/assets/objects/blocks/block-decoration.png'),
    ('top_block', 'Top block', 'block', 6, 0, false, false, '/assets/objects/blocks/block-top.png'),
    ('column_block', 'Column block', 'block', 7, 0, false, false, '/assets/objects/blocks/block-column.png'),
    ('spike', 'Spike', 'hazard', 8, 0, false, false, '/assets/objects/hazards/spike.png'),
    ('ground_spike', 'Ground spike', 'hazard', 9, -12.5, false, false, '/assets/objects/hazards/spike-ground.png'),
    ('blue_portal', 'Blue gravity portal', 'portal', 10, 0, false, false, '/assets/objects/portals/portal-gravity-normal.png'),
    ('yellow_portal', 'Yellow gravity portal', 'portal', 11, 0, false, false, '/assets/objects/portals/portal-gravity-reversed.png'),
    ('cube_portal', 'Cube portal', 'portal', 12, 0, false, false, '/assets/objects/portals/portal-cube.png'),
    ('ship_portal', 'Ship portal', 'portal', 13, 0, false, false, '/assets/objects/portals/portal-ship.png'),
    ('yellow_pad', 'Yellow jump pad', 'pad', 35, 0, false, false, '/assets/objects/pads/pad-jump-yellow.png'),
    ('yellow_orb', 'Yellow jump orb', 'orb', 36, 0, false, false, '/assets/objects/orbs/orb-jump-yellow.png'),
    ('flat_spike', 'Flat spike', 'hazard', 39, -9, false, false, '/assets/objects/hazards/spike-flat.png'),
    ('half_block', 'Half block', 'block', 40, 8, false, false, '/assets/objects/blocks/block-half.png'),
    ('bg_color_trigger', 'Background color trigger', 'trigger', 899, 0, true, true, '/assets/objects/triggers/trigger-color.png'),
    ('g1_color_trigger', 'Ground color trigger', 'trigger', 899, 0, true, true, '/assets/objects/triggers/trigger-color.png')
ON CONFLICT (key) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    category = EXCLUDED.category,
    geometry_dash_object_id = EXCLUDED.geometry_dash_object_id,
    y_offset = EXCLUDED.y_offset,
    has_color_settings = EXCLUDED.has_color_settings,
    has_duration_setting = EXCLUDED.has_duration_setting,
    asset_path = EXCLUDED.asset_path,
    is_active = true;

COMMIT;
