INSERT INTO schema_info(version, created_utc)
VALUES (8, '2026-09-14T00:00:00Z');

INSERT INTO faction(faction_id, faction_key, display_name) VALUES
    (0, 'NATO', 'NATO'),
    (1, 'WARSAW_PACT', 'Warsaw Pact');

INSERT INTO nation(nation_id, faction_id, nation_key, display_name) VALUES
    (0, 0, 'USA', 'USA'),
    (1, 0, 'WEST_GERMANY', 'West Germany'),
    (2, 0, 'UNITED_KINGDOM', 'United Kingdom'),
    (3, 0, 'NETHERLANDS', 'Netherlands'),
    (4, 0, 'BELGIUM', 'Belgium'),
    (5, 0, 'FRANCE', 'France'),
    (6, 1, 'USSR', 'USSR'),
    (7, 1, 'WARSAW_PACT_ALLIES', 'Warsaw Pact allies');

INSERT INTO echelon(echelon_id, echelon_key, display_name) VALUES
    (0, 'team', 'Team'),
    (1, 'platoon', 'Platoon'),
    (2, 'battery', 'Battery'),
    (3, 'section', 'Section'),
    (4, 'troop', 'Troop'),
    (5, 'fire_unit', 'Fire unit');

INSERT INTO unit_category(category_id, category_key, display_name) VALUES
    (0, 'command', 'Command'),
    (1, 'tank', 'Tank'),
    (2, 'mechanized_infantry', 'Mechanized infantry'),
    (3, 'motorized_infantry', 'Motorized infantry'),
    (4, 'reconnaissance', 'Reconnaissance'),
    (5, 'anti_tank', 'Anti-tank'),
    (6, 'artillery', 'Artillery'),
    (7, 'air_defense', 'Air defense'),
    (8, 'engineer', 'Engineer'),
    (9, 'rocket_artillery', 'Rocket artillery');

INSERT INTO mobility_class(mobility_id, mobility_key, display_name) VALUES
    (0, 'tracked', 'Tracked'),
    (1, 'wheeled', 'Wheeled'),
    (2, 'foot', 'Foot'),
    (3, 'towed', 'Towed'),
    (4, 'mixed', 'Mixed');

INSERT INTO terrain_type(
    terrain_id, terrain_key, display_name, cover_rating, concealment_rating
) VALUES
    (0, 'clear', 'Clear', 0, 0),
    (1, 'woods', 'Woods', 4, 7),
    (2, 'rough', 'Rough', 3, 2),
    (3, 'marsh', 'Marsh', 1, 3),
    (4, 'water', 'Water', 0, 0),
    (5, 'urban', 'Urban', 7, 7),
    (6, 'cultivated', 'Cultivated', 1, 2);

INSERT INTO terrain_movement_cost(
    mobility_id, terrain_id, passable, quick_cost, tactical_cost, hunt_cost
)
SELECT m.mobility_id, t.terrain_id,
       CASE WHEN t.terrain_key = 'water' OR
                      (m.mobility_key IN ('wheeled', 'towed') AND t.terrain_key = 'marsh')
            THEN 0 ELSE 1 END,
       CASE
           WHEN t.terrain_key = 'water' OR
                (m.mobility_key IN ('wheeled', 'towed') AND t.terrain_key = 'marsh') THEN NULL
           WHEN m.mobility_key = 'tracked' THEN
                CASE t.terrain_key WHEN 'clear' THEN 8 WHEN 'cultivated' THEN 10
                     WHEN 'woods' THEN 22 WHEN 'rough' THEN 18 WHEN 'marsh' THEN 34
                     WHEN 'urban' THEN 16 ELSE 12 END
           WHEN m.mobility_key = 'wheeled' THEN
                CASE t.terrain_key WHEN 'clear' THEN 10 WHEN 'cultivated' THEN 14
                     WHEN 'woods' THEN 32 WHEN 'rough' THEN 28 WHEN 'urban' THEN 18 ELSE 16 END
           WHEN m.mobility_key = 'foot' THEN
                CASE t.terrain_key WHEN 'clear' THEN 16 WHEN 'cultivated' THEN 17
                     WHEN 'woods' THEN 20 WHEN 'rough' THEN 20 WHEN 'marsh' THEN 27
                     WHEN 'urban' THEN 18 ELSE 20 END
           WHEN m.mobility_key = 'towed' THEN
                CASE t.terrain_key WHEN 'clear' THEN 12 WHEN 'cultivated' THEN 17
                     WHEN 'woods' THEN 36 WHEN 'rough' THEN 32 WHEN 'urban' THEN 22 ELSE 20 END
           ELSE
                CASE t.terrain_key WHEN 'clear' THEN 11 WHEN 'cultivated' THEN 15
                     WHEN 'woods' THEN 30 WHEN 'rough' THEN 27 WHEN 'marsh' THEN 38
                     WHEN 'urban' THEN 20 ELSE 18 END
       END,
       CASE
           WHEN t.terrain_key = 'water' OR
                (m.mobility_key IN ('wheeled', 'towed') AND t.terrain_key = 'marsh') THEN NULL
           WHEN m.mobility_key = 'tracked' THEN
                CASE t.terrain_key WHEN 'clear' THEN 10 WHEN 'cultivated' THEN 12
                     WHEN 'woods' THEN 24 WHEN 'rough' THEN 20 WHEN 'marsh' THEN 38
                     WHEN 'urban' THEN 18 ELSE 14 END
           WHEN m.mobility_key = 'wheeled' THEN
                CASE t.terrain_key WHEN 'clear' THEN 12 WHEN 'cultivated' THEN 16
                     WHEN 'woods' THEN 35 WHEN 'rough' THEN 30 WHEN 'urban' THEN 20 ELSE 18 END
           WHEN m.mobility_key = 'foot' THEN
                CASE t.terrain_key WHEN 'clear' THEN 18 WHEN 'cultivated' THEN 19
                     WHEN 'woods' THEN 21 WHEN 'rough' THEN 22 WHEN 'marsh' THEN 30
                     WHEN 'urban' THEN 19 ELSE 22 END
           WHEN m.mobility_key = 'towed' THEN
                CASE t.terrain_key WHEN 'clear' THEN 14 WHEN 'cultivated' THEN 19
                     WHEN 'woods' THEN 40 WHEN 'rough' THEN 35 WHEN 'urban' THEN 24 ELSE 22 END
           ELSE
                CASE t.terrain_key WHEN 'clear' THEN 13 WHEN 'cultivated' THEN 17
                     WHEN 'woods' THEN 33 WHEN 'rough' THEN 30 WHEN 'marsh' THEN 42
                     WHEN 'urban' THEN 22 ELSE 20 END
       END,
       CASE
           WHEN t.terrain_key = 'water' OR
                (m.mobility_key IN ('wheeled', 'towed') AND t.terrain_key = 'marsh') THEN NULL
           WHEN m.mobility_key = 'tracked' THEN
                CASE t.terrain_key WHEN 'clear' THEN 15 WHEN 'cultivated' THEN 16
                     WHEN 'woods' THEN 19 WHEN 'rough' THEN 23 WHEN 'marsh' THEN 43
                     WHEN 'urban' THEN 20 ELSE 18 END
           WHEN m.mobility_key = 'wheeled' THEN
                CASE t.terrain_key WHEN 'clear' THEN 17 WHEN 'cultivated' THEN 20
                     WHEN 'woods' THEN 27 WHEN 'rough' THEN 34 WHEN 'urban' THEN 22 ELSE 22 END
           WHEN m.mobility_key = 'foot' THEN
                CASE t.terrain_key WHEN 'clear' THEN 22 WHEN 'cultivated' THEN 22
                     WHEN 'woods' THEN 19 WHEN 'rough' THEN 24 WHEN 'marsh' THEN 34
                     WHEN 'urban' THEN 20 ELSE 25 END
           WHEN m.mobility_key = 'towed' THEN
                CASE t.terrain_key WHEN 'clear' THEN 20 WHEN 'cultivated' THEN 23
                     WHEN 'woods' THEN 34 WHEN 'rough' THEN 39 WHEN 'urban' THEN 27 ELSE 27 END
           ELSE
                CASE t.terrain_key WHEN 'clear' THEN 18 WHEN 'cultivated' THEN 21
                     WHEN 'woods' THEN 27 WHEN 'rough' THEN 34 WHEN 'marsh' THEN 47
                     WHEN 'urban' THEN 24 ELSE 24 END
       END
FROM mobility_class m CROSS JOIN terrain_type t;

INSERT INTO road_movement_cost(
    mobility_id, road_class, quick_cost, tactical_cost, hunt_cost
)
SELECT mobility_id, road_class,
       CASE mobility_key WHEN 'foot' THEN 14 ELSE
            CASE road_class WHEN 3 THEN 4 WHEN 2 THEN 5 ELSE 7 END END,
       CASE mobility_key WHEN 'foot' THEN 16 ELSE
            CASE road_class WHEN 3 THEN 6 WHEN 2 THEN 7 ELSE 9 END END,
       CASE mobility_key WHEN 'foot' THEN 19 ELSE
            CASE road_class WHEN 3 THEN 9 WHEN 2 THEN 10 ELSE 12 END END
FROM mobility_class CROSS JOIN (SELECT 1 road_class UNION ALL SELECT 2 UNION ALL SELECT 3);

INSERT INTO water_crossing_cost(
    mobility_id, river_class, crossing_type, requires_amphibious,
    quick_cost, tactical_cost, hunt_cost
)
SELECT mobility_id, river_class, crossing_type,
       CASE WHEN crossing_type = 'none' AND river_class >= 2 THEN 1 ELSE 0 END,
       CASE crossing_type WHEN 'bridge' THEN 2 WHEN 'ford' THEN 10
            ELSE CASE river_class WHEN 1 THEN 6 WHEN 2 THEN 30 ELSE 42 END END,
       CASE crossing_type WHEN 'bridge' THEN 3 WHEN 'ford' THEN 12
            ELSE CASE river_class WHEN 1 THEN 8 WHEN 2 THEN 34 ELSE 48 END END,
       CASE crossing_type WHEN 'bridge' THEN 4 WHEN 'ford' THEN 15
            ELSE CASE river_class WHEN 1 THEN 10 WHEN 2 THEN 38 ELSE 54 END END
FROM mobility_class
CROSS JOIN (SELECT 1 river_class UNION ALL SELECT 2 UNION ALL SELECT 3)
CROSS JOIN (SELECT 'none' crossing_type UNION ALL SELECT 'bridge' UNION ALL SELECT 'ford');

INSERT INTO movement_parameter(parameter_key, value_integer, notes) VALUES
    ('elevation_step_m', 25, 'Elevation interval used to calculate movement penalties.'),
    ('uphill_cost_per_step', 2, 'Additional cost per elevation step when climbing.'),
    ('downhill_cost_per_step', 1, 'Additional cost per elevation step when descending.'),
    ('road_access_penalty', 8, 'Cost for entering, leaving, or cutting across a road hex without following its connected road edge.'),
    ('minimum_step_cost', 4, 'Lower bound used by the A-star heuristic.');

INSERT INTO formation_kind(formation_kind_id, formation_kind_key, display_name) VALUES
    (0, 'regiment', 'Regiment'),
    (1, 'battalion', 'Battalion'),
    (2, 'squadron', 'Squadron'),
    (3, 'company', 'Company'),
    (4, 'company_team', 'Company team'),
    (5, 'platoon', 'Platoon'),
    (6, 'detachment', 'Detachment');
