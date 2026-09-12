INSERT INTO scenario(
    scenario_id, scenario_key, display_name, map_id, scenario_year,
    turn_minutes, cursor_x, cursor_y, notes
) VALUES (
    0, 'a2_command_post', 'Point Alpha Data Demonstration', 1, 1985,
    15, 19, 48, 'Illustrative unit placement on the W1 production map.'
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
        'team_alpha_hq', 'Team Alpha Headquarters', 19, 48, 10, 82, 0, 92),
    (1, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_cavalry_platoon_m3'),
        'team_alpha_cav_1', '1st Cavalry Platoon', 24, 44, 10, 80, 0, 90),
    (2, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_cavalry_platoon_m3'),
        'team_alpha_cav_2', '2nd Cavalry Platoon', 26, 46, 9, 76, 1, 86),
    (3, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_tank_platoon_m1'),
        'team_alpha_tank', 'Tank Platoon', 24, 48, 10, 84, 0, 94),
    (4, 0, 1, (SELECT unit_type_id FROM unit_type WHERE type_key='us_atgm_team_tow'),
        'team_alpha_atgm', 'TOW Team', 21, 46, 10, 78, 0, 88),
    (5, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_hq_team_bmp1k'),
        'su_tank_company_hq', 'Tank Company Headquarters', 70, 16, 10, 74, 0, 88),
    (6, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_1', '1st Tank Platoon', 65, 20, 10, 74, 0, 90),
    (7, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_2', '2nd Tank Platoon', 67, 20, 10, 72, 0, 88),
    (8, 0, 3, (SELECT unit_type_id FROM unit_type WHERE type_key='su_tank_platoon_t64b'),
        'su_tank_platoon_3', '3rd Tank Platoon', 70, 20, 9, 68, 1, 82),
    (9, 0, 4, (SELECT unit_type_id FROM unit_type WHERE type_key='su_motor_rifle_platoon_bmp2'),
        'su_attached_motor_rifle', 'Attached Motor-Rifle Platoon', 65, 24, 10, 72, 0, 86),
    (10, 0, 4, (SELECT unit_type_id FROM unit_type WHERE type_key='su_atgm_team_at4_at5'),
        'su_attached_atgm', 'Attached ATGM Team', 67, 24, 10, 70, 0, 84);
