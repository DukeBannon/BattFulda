INSERT INTO map(map_id, map_key, display_name, width, height, cell_size_m, source_path)
VALUES (0, 'fulda_gap', 'Fulda Gap', 20, 15, 500, 'data/maps/fulda_gap.json');

INSERT INTO scenario(
    scenario_id, scenario_key, display_name, map_id, scenario_year,
    turn_minutes, cursor_x, cursor_y, notes
) VALUES (
    0, 'a2_command_post', 'Fulda Command Post', 0, 1985,
    15, 9, 7, 'A2.1 database and order-of-battle demonstration.'
);

INSERT INTO formation(
    formation_id, scenario_id, parent_formation_id, faction_id, nation_id,
    formation_kind_id, formation_key, display_name, command_rating,
    base_morale, sort_order
) VALUES
    (0, 0, NULL, 0, 0, 2, 'nato_1_11_acr', '1st Squadron, 11th ACR', 9, 82, 10),
    (1, 0, 0,    0, 0, 4, 'nato_team_alpha', 'Team Alpha', 8, 78, 20),
    (2, 0, NULL, 1, 6, 1, 'su_tank_battalion', '1st Tank Battalion', 8, 76, 30),
    (3, 0, 2,    1, 6, 3, 'su_1st_tank_company', '1st Tank Company', 7, 72, 40),
    (4, 0, 2,    1, 6, 4, 'su_forward_detachment', 'Forward Detachment', 8, 75, 50);

INSERT INTO scenario_unit(
    scenario_unit_id, scenario_id, formation_id, unit_type_id, unit_key,
    display_name, x, y, strength, morale, suppression, readiness
) VALUES
    (0, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_hq_team_m577'),
        'team_alpha_hq', 'Team Alpha Headquarters', 3, 12, 10, 82, 0, 92),
    (1, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_cavalry_platoon_m3'),
        'team_alpha_cav_1', '1st Cavalry Platoon', 5, 10, 10, 80, 0, 90),
    (2, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_cavalry_platoon_m3'),
        'team_alpha_cav_2', '2nd Cavalry Platoon', 6, 11, 9, 76, 1, 86),
    (3, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_tank_platoon_m1'),
        'team_alpha_tank', 'Tank Platoon', 5, 12, 10, 84, 0, 94),
    (4, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_atgm_team_tow'),
        'team_alpha_atgm', 'TOW Team', 4, 11, 10, 78, 0, 88),
    (5, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_hq_team_bmp1k'),
        'su_tank_company_hq', 'Tank Company Headquarters', 16, 3, 10, 74, 0, 88),
    (6, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_1', '1st Tank Platoon', 15, 4, 10, 74, 0, 90),
    (7, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_2', '2nd Tank Platoon', 16, 4, 10, 72, 0, 88),
    (8, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_3', '3rd Tank Platoon', 17, 4, 9, 68, 1, 82),
    (9, 0, 4, (SELECT unit_type_id FROM unit_type WHERE type_key='su_motor_rifle_platoon_bmp2'),
        'su_attached_motor_rifle', 'Attached Motor-Rifle Platoon', 15, 5, 10, 72, 0, 86),
    (10, 0, 4, (SELECT unit_type_id FROM unit_type WHERE type_key='su_atgm_team_at4_at5'),
        'su_attached_atgm', 'Attached ATGM Team', 16, 5, 10, 70, 0, 84);
