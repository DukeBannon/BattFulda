INSERT INTO schema_info(version, created_utc)
VALUES (5, '2026-09-11T00:00:00Z');

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
    (5, 'urban', 'Urban', 7, 7);

INSERT INTO formation_kind(formation_kind_id, formation_kind_key, display_name) VALUES
    (0, 'regiment', 'Regiment'),
    (1, 'battalion', 'Battalion'),
    (2, 'squadron', 'Squadron'),
    (3, 'company', 'Company'),
    (4, 'company_team', 'Company team'),
    (5, 'platoon', 'Platoon'),
    (6, 'detachment', 'Detachment');
