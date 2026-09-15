PRAGMA foreign_keys = ON;

CREATE TABLE schema_info (
    version INTEGER NOT NULL CHECK (version > 0),
    created_utc TEXT NOT NULL
);

CREATE TABLE faction (
    faction_id INTEGER PRIMARY KEY,
    faction_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL UNIQUE
);

CREATE TABLE nation (
    nation_id INTEGER PRIMARY KEY,
    faction_id INTEGER NOT NULL REFERENCES faction(faction_id),
    nation_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL UNIQUE
);

CREATE TABLE echelon (
    echelon_id INTEGER PRIMARY KEY,
    echelon_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL
);

CREATE TABLE unit_category (
    category_id INTEGER PRIMARY KEY,
    category_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL
);

CREATE TABLE mobility_class (
    mobility_id INTEGER PRIMARY KEY,
    mobility_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL
);

CREATE TABLE unit_type (
    unit_type_id INTEGER PRIMARY KEY CHECK (unit_type_id BETWEEN 0 AND 254),
    type_key TEXT NOT NULL UNIQUE,
    faction_id INTEGER NOT NULL REFERENCES faction(faction_id),
    nation_id INTEGER NOT NULL REFERENCES nation(nation_id),
    echelon_id INTEGER NOT NULL REFERENCES echelon(echelon_id),
    category_id INTEGER NOT NULL REFERENCES unit_category(category_id),
    mobility_id INTEGER NOT NULL REFERENCES mobility_class(mobility_id),
    display_name TEXT NOT NULL,
    equipment TEXT NOT NULL,
    role TEXT NOT NULL,
    move_points INTEGER NOT NULL CHECK (move_points BETWEEN 0 AND 15),
    hard_attack INTEGER NOT NULL CHECK (hard_attack BETWEEN 0 AND 15),
    soft_attack INTEGER NOT NULL CHECK (soft_attack BETWEEN 0 AND 15),
    defense INTEGER NOT NULL CHECK (defense BETWEEN 0 AND 15),
    range_cells INTEGER NOT NULL CHECK (range_cells BETWEEN 0 AND 255),
    recon INTEGER NOT NULL CHECK (recon BETWEEN 0 AND 15),
    command INTEGER NOT NULL CHECK (command BETWEEN 0 AND 15),
    amphibious INTEGER NOT NULL CHECK (amphibious IN (0, 1)),
    availability_1985 TEXT NOT NULL CHECK (
        availability_1985 IN ('common', 'fielding', 'limited', 'second_line')
    ),
    notes TEXT NOT NULL DEFAULT '',
    source_url TEXT NOT NULL CHECK (source_url LIKE 'https://%'),
    rating_status TEXT NOT NULL CHECK (
        rating_status IN ('provisional_design', 'reviewed', 'final')
    )
);

CREATE TABLE formation_kind (
    formation_kind_id INTEGER PRIMARY KEY,
    formation_kind_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL
);

CREATE TABLE map (
    map_id INTEGER PRIMARY KEY,
    map_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    width INTEGER NOT NULL CHECK (width BETWEEN 1 AND 255),
    height INTEGER NOT NULL CHECK (height BETWEEN 1 AND 255),
    cell_size_m INTEGER NOT NULL CHECK (cell_size_m > 0),
    crs TEXT NOT NULL,
    origin_easting_m INTEGER,
    origin_northing_m INTEGER,
    extent_width_m INTEGER CHECK (extent_width_m IS NULL OR extent_width_m > 0),
    extent_height_m INTEGER CHECK (extent_height_m IS NULL OR extent_height_m > 0),
    geographic_status TEXT NOT NULL CHECK (
        geographic_status IN ('abstract', 'elevation_draft', 'feature_draft', 'reviewed')
    ),
    source_path TEXT NOT NULL UNIQUE
);

CREATE TABLE map_source (
    map_source_id INTEGER PRIMARY KEY,
    map_id INTEGER NOT NULL REFERENCES map(map_id) ON DELETE CASCADE,
    source_key TEXT NOT NULL,
    display_name TEXT NOT NULL,
    source_url TEXT NOT NULL CHECK (source_url LIKE 'https://%'),
    license_name TEXT NOT NULL,
    attribution TEXT NOT NULL,
    source_date TEXT NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    UNIQUE (map_id, source_key)
);

CREATE TABLE terrain_type (
    terrain_id INTEGER PRIMARY KEY CHECK (terrain_id BETWEEN 0 AND 254),
    terrain_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL UNIQUE,
    cover_rating INTEGER NOT NULL CHECK (cover_rating BETWEEN 0 AND 15),
    concealment_rating INTEGER NOT NULL CHECK (concealment_rating BETWEEN 0 AND 15)
);

CREATE TABLE terrain_movement_cost (
    mobility_id INTEGER NOT NULL REFERENCES mobility_class(mobility_id),
    terrain_id INTEGER NOT NULL REFERENCES terrain_type(terrain_id),
    passable INTEGER NOT NULL CHECK (passable IN (0, 1)),
    quick_cost INTEGER CHECK (quick_cost IS NULL OR quick_cost > 0),
    tactical_cost INTEGER CHECK (tactical_cost IS NULL OR tactical_cost > 0),
    hunt_cost INTEGER CHECK (hunt_cost IS NULL OR hunt_cost > 0),
    PRIMARY KEY (mobility_id, terrain_id),
    CHECK (
        (passable = 0 AND quick_cost IS NULL AND tactical_cost IS NULL AND hunt_cost IS NULL)
        OR
        (passable = 1 AND quick_cost IS NOT NULL AND tactical_cost IS NOT NULL AND hunt_cost IS NOT NULL)
    )
);

CREATE TABLE road_movement_cost (
    mobility_id INTEGER NOT NULL REFERENCES mobility_class(mobility_id),
    road_class INTEGER NOT NULL CHECK (road_class BETWEEN 1 AND 3),
    quick_cost INTEGER NOT NULL CHECK (quick_cost > 0),
    tactical_cost INTEGER NOT NULL CHECK (tactical_cost > 0),
    hunt_cost INTEGER NOT NULL CHECK (hunt_cost > 0),
    PRIMARY KEY (mobility_id, road_class)
);

CREATE TABLE water_crossing_cost (
    mobility_id INTEGER NOT NULL REFERENCES mobility_class(mobility_id),
    river_class INTEGER NOT NULL CHECK (river_class BETWEEN 1 AND 3),
    crossing_type TEXT NOT NULL CHECK (crossing_type IN ('none', 'bridge', 'ford')),
    requires_amphibious INTEGER NOT NULL CHECK (requires_amphibious IN (0, 1)),
    quick_cost INTEGER NOT NULL CHECK (quick_cost > 0),
    tactical_cost INTEGER NOT NULL CHECK (tactical_cost > 0),
    hunt_cost INTEGER NOT NULL CHECK (hunt_cost > 0),
    PRIMARY KEY (mobility_id, river_class, crossing_type)
);

CREATE TABLE movement_parameter (
    parameter_key TEXT PRIMARY KEY,
    value_integer INTEGER NOT NULL CHECK (value_integer >= 0),
    notes TEXT NOT NULL DEFAULT ''
);

CREATE TABLE map_cell (
    map_id INTEGER NOT NULL REFERENCES map(map_id) ON DELETE CASCADE,
    x INTEGER NOT NULL CHECK (x BETWEEN 0 AND 254),
    y INTEGER NOT NULL CHECK (y BETWEEN 0 AND 254),
    terrain_id INTEGER NOT NULL REFERENCES terrain_type(terrain_id),
    elevation_m INTEGER NOT NULL CHECK (elevation_m BETWEEN -500 AND 9000),
    road_class INTEGER NOT NULL DEFAULT 0 CHECK (road_class BETWEEN 0 AND 3),
    road_links INTEGER NOT NULL DEFAULT 0 CHECK (road_links BETWEEN 0 AND 63),
    river_class INTEGER NOT NULL DEFAULT 0 CHECK (river_class BETWEEN 0 AND 3),
    river_links INTEGER NOT NULL DEFAULT 0 CHECK (river_links BETWEEN 0 AND 63),
    settlement_level INTEGER NOT NULL DEFAULT 0 CHECK (settlement_level BETWEEN 0 AND 3),
    has_bridge INTEGER NOT NULL DEFAULT 0 CHECK (has_bridge IN (0, 1)),
    source_status TEXT NOT NULL CHECK (
        source_status IN ('abstract', 'elevation_only', 'draft', 'reviewed')
    ),
    notes TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (map_id, x, y)
);

CREATE TABLE map_edge (
    map_id INTEGER NOT NULL REFERENCES map(map_id) ON DELETE CASCADE,
    x INTEGER NOT NULL CHECK (x BETWEEN 0 AND 254),
    y INTEGER NOT NULL CHECK (y BETWEEN 0 AND 254),
    direction INTEGER NOT NULL CHECK (direction IN (1, 2, 4, 8, 16, 32)),
    neighbor_x INTEGER NOT NULL CHECK (neighbor_x BETWEEN 0 AND 254),
    neighbor_y INTEGER NOT NULL CHECK (neighbor_y BETWEEN 0 AND 254),
    road_class INTEGER NOT NULL DEFAULT 0 CHECK (road_class BETWEEN 0 AND 3),
    river_class INTEGER NOT NULL DEFAULT 0 CHECK (river_class BETWEEN 0 AND 3),
    crossing_type TEXT NOT NULL DEFAULT 'none' CHECK (
        crossing_type IN ('none', 'bridge', 'ford')
    ),
    source_status TEXT NOT NULL CHECK (source_status IN ('draft', 'reviewed')),
    PRIMARY KEY (map_id, x, y, direction),
    UNIQUE (map_id, x, y, neighbor_x, neighbor_y)
);

CREATE TABLE map_crossing_edge (
    map_id INTEGER NOT NULL REFERENCES map(map_id) ON DELETE CASCADE,
    x INTEGER NOT NULL CHECK (x BETWEEN 0 AND 254),
    y INTEGER NOT NULL CHECK (y BETWEEN 0 AND 254),
    neighbor_x INTEGER NOT NULL CHECK (neighbor_x BETWEEN 0 AND 254),
    neighbor_y INTEGER NOT NULL CHECK (neighbor_y BETWEEN 0 AND 254),
    river_class INTEGER NOT NULL CHECK (river_class BETWEEN 1 AND 3),
    crossing_type TEXT NOT NULL CHECK (crossing_type IN ('none', 'bridge', 'ford')),
    source_status TEXT NOT NULL CHECK (source_status IN ('derived', 'draft', 'reviewed')),
    PRIMARY KEY (map_id, x, y, neighbor_x, neighbor_y)
);

CREATE TABLE map_feature (
    map_id INTEGER NOT NULL REFERENCES map(map_id) ON DELETE CASCADE,
    feature_id INTEGER NOT NULL,
    feature_type TEXT NOT NULL CHECK (feature_type IN ('road', 'river', 'bridge')),
    feature_class INTEGER NOT NULL CHECK (feature_class BETWEEN 1 AND 3),
    source_status TEXT NOT NULL CHECK (source_status IN ('draft', 'reviewed')),
    PRIMARY KEY (map_id, feature_id)
);

CREATE TABLE map_feature_vertex (
    map_id INTEGER NOT NULL,
    feature_id INTEGER NOT NULL,
    sequence INTEGER NOT NULL CHECK (sequence >= 0),
    easting_m REAL NOT NULL,
    northing_m REAL NOT NULL,
    PRIMARY KEY (map_id, feature_id, sequence),
    FOREIGN KEY (map_id, feature_id)
        REFERENCES map_feature(map_id, feature_id) ON DELETE CASCADE
);

CREATE TABLE scenario (
    scenario_id INTEGER PRIMARY KEY CHECK (scenario_id BETWEEN 0 AND 254),
    scenario_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    map_id INTEGER NOT NULL REFERENCES map(map_id),
    scenario_year INTEGER NOT NULL CHECK (scenario_year BETWEEN 1945 AND 2100),
    start_datetime TEXT NOT NULL CHECK (datetime(start_datetime) IS NOT NULL),
    turn_minutes INTEGER NOT NULL CHECK (turn_minutes BETWEEN 1 AND 255),
    cursor_x INTEGER NOT NULL CHECK (cursor_x BETWEEN 0 AND 254),
    cursor_y INTEGER NOT NULL CHECK (cursor_y BETWEEN 0 AND 254),
    notes TEXT NOT NULL DEFAULT ''
);

CREATE TABLE formation (
    scenario_id INTEGER NOT NULL REFERENCES scenario(scenario_id) ON DELETE CASCADE,
    formation_id INTEGER NOT NULL CHECK (formation_id BETWEEN 0 AND 254),
    parent_formation_id INTEGER,
    faction_id INTEGER NOT NULL REFERENCES faction(faction_id),
    nation_id INTEGER NOT NULL REFERENCES nation(nation_id),
    formation_kind_id INTEGER NOT NULL REFERENCES formation_kind(formation_kind_id),
    formation_key TEXT NOT NULL,
    display_name TEXT NOT NULL,
    command_rating INTEGER NOT NULL CHECK (command_rating BETWEEN 0 AND 15),
    base_morale INTEGER NOT NULL CHECK (base_morale BETWEEN 0 AND 100),
    sort_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (scenario_id, formation_id),
    FOREIGN KEY (scenario_id, parent_formation_id)
        REFERENCES formation(scenario_id, formation_id),
    UNIQUE (scenario_id, formation_key)
);

CREATE TABLE scenario_unit (
    scenario_id INTEGER NOT NULL REFERENCES scenario(scenario_id) ON DELETE CASCADE,
    scenario_unit_id INTEGER NOT NULL CHECK (scenario_unit_id BETWEEN 0 AND 254),
    formation_id INTEGER NOT NULL,
    unit_type_id INTEGER NOT NULL REFERENCES unit_type(unit_type_id),
    unit_key TEXT NOT NULL,
    display_name TEXT NOT NULL,
    x INTEGER NOT NULL CHECK (x BETWEEN 0 AND 254),
    y INTEGER NOT NULL CHECK (y BETWEEN 0 AND 254),
    strength INTEGER NOT NULL CHECK (strength BETWEEN 0 AND 10),
    morale INTEGER NOT NULL CHECK (morale BETWEEN 0 AND 100),
    suppression INTEGER NOT NULL CHECK (suppression BETWEEN 0 AND 10),
    readiness INTEGER NOT NULL CHECK (readiness BETWEEN 0 AND 100),
    PRIMARY KEY (scenario_id, scenario_unit_id),
    FOREIGN KEY (scenario_id, formation_id)
        REFERENCES formation(scenario_id, formation_id) ON DELETE CASCADE,
    UNIQUE (scenario_id, unit_key)
);

CREATE INDEX idx_unit_type_nation ON unit_type(nation_id);
CREATE INDEX idx_unit_type_category ON unit_type(category_id);
CREATE INDEX idx_map_cell_terrain ON map_cell(map_id, terrain_id);
CREATE INDEX idx_formation_parent ON formation(scenario_id, parent_formation_id);
CREATE INDEX idx_scenario_unit_formation ON scenario_unit(scenario_id, formation_id);
CREATE INDEX idx_scenario_unit_position ON scenario_unit(scenario_id, x, y);

CREATE VIEW v_unit_type AS
SELECT
    ut.unit_type_id,
    ut.type_key,
    f.display_name AS faction,
    n.display_name AS country,
    e.echelon_key AS echelon,
    c.category_key AS category,
    ut.display_name,
    ut.equipment,
    ut.role,
    m.mobility_key AS mobility,
    ut.move_points,
    ut.hard_attack,
    ut.soft_attack,
    ut.defense,
    ut.range_cells,
    ut.recon,
    ut.command,
    ut.amphibious,
    ut.availability_1985,
    ut.notes,
    ut.source_url,
    ut.rating_status
FROM unit_type ut
JOIN faction f ON f.faction_id = ut.faction_id
JOIN nation n ON n.nation_id = ut.nation_id
JOIN echelon e ON e.echelon_id = ut.echelon_id
JOIN unit_category c ON c.category_id = ut.category_id
JOIN mobility_class m ON m.mobility_id = ut.mobility_id;

CREATE VIEW v_scenario_order_of_battle AS
SELECT
    s.scenario_key,
    su.scenario_unit_id,
    su.unit_key,
    su.display_name AS unit_name,
    fo.display_name AS formation_name,
    parent.display_name AS parent_formation,
    ut.type_key,
    ut.display_name AS unit_type,
    f.display_name AS faction,
    n.display_name AS country,
    su.x,
    su.y,
    su.strength,
    su.morale,
    su.suppression,
    su.readiness,
    ut.amphibious,
    m.mobility_key AS mobility,
    ut.move_points
FROM scenario_unit su
JOIN scenario s ON s.scenario_id = su.scenario_id
JOIN formation fo
  ON fo.scenario_id = su.scenario_id AND fo.formation_id = su.formation_id
LEFT JOIN formation parent
  ON parent.scenario_id = fo.scenario_id
 AND parent.formation_id = fo.parent_formation_id
JOIN unit_type ut ON ut.unit_type_id = su.unit_type_id
JOIN mobility_class m ON m.mobility_id = ut.mobility_id
JOIN faction f ON f.faction_id = fo.faction_id
JOIN nation n ON n.nation_id = fo.nation_id;
