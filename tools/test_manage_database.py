import csv
from pathlib import Path
import tempfile
import unittest

import manage_database


class DatabaseTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.database = self.root / "test.sqlite"
        manage_database.initialize_database(self.database)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_seeded_relationships_and_state(self) -> None:
        counts = manage_database.validate_database(self.database)
        self.assertEqual(counts, {
            "unit_types": 64,
            "scenarios": 1,
            "formations": 5,
            "scenario_units": 11,
            "maps": 2,
            "map_cells": 7360,
            "map_edges": 2411,
            "map_features": 459,
        })
        connection = manage_database.connect(self.database)
        try:
            tank = connection.execute("""
                SELECT strength, morale, suppression, readiness
                FROM scenario_unit WHERE unit_key = 'su_tank_platoon_3'
            """).fetchone()
        finally:
            connection.close()
        self.assertEqual(tuple(tank), (9, 68, 1, 82))

        connection = manage_database.connect(self.database)
        try:
            crossing = connection.execute("""
                SELECT
                    (SELECT amphibious FROM unit_type WHERE type_key = 'su_motor_rifle_platoon_bmp2'),
                    (SELECT amphibious FROM unit_type WHERE type_key = 'us_tank_platoon_m1')
            """).fetchone()
        finally:
            connection.close()
        self.assertEqual(tuple(crossing), (1, 0))

        connection = manage_database.connect(self.database)
        try:
            terrain = connection.execute("""
                SELECT MIN(elevation_m), MAX(elevation_m), COUNT(*),
                       COUNT(DISTINCT source_status)
                FROM map_cell WHERE map_id = 1
            """).fetchone()
        finally:
            connection.close()
        self.assertEqual(tuple(terrain), (232, 713, 7360, 1))

        connection = manage_database.connect(self.database)
        try:
            features = connection.execute("""
                SELECT
                    SUM(CASE WHEN terrain_id = 1 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN terrain_id = 5 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN terrain_id = 6 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN road_class > 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN road_links > 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN river_class > 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN river_links > 0 THEN 1 ELSE 0 END),
                    SUM(has_bridge)
                FROM map_cell WHERE map_id = 1
            """).fetchone()
        finally:
            connection.close()
        self.assertTrue(all(value > 0 for value in features))

    def test_w1_hex_topology_and_feature_paths(self) -> None:
        connection = manage_database.connect(self.database)
        try:
            map_row = connection.execute("""
                SELECT width, height, cell_size_m, extent_width_m, extent_height_m
                FROM map WHERE map_key = 'point_alpha_corridor'
            """).fetchone()
            cultivated_cells = connection.execute("""
                SELECT COUNT(*) FROM map_cell
                WHERE map_id = 1 AND terrain_id = 6
            """).fetchone()[0]
            road_classes = {
                row[0] for row in connection.execute(
                    "SELECT DISTINCT road_class FROM map_edge WHERE road_class > 0"
                )
            }
            river_classes = {
                row[0] for row in connection.execute(
                    "SELECT DISTINCT river_class FROM map_edge WHERE river_class > 0"
                )
            }
            bridge_paths = connection.execute("""
                SELECT COUNT(*) FROM map_feature WHERE feature_type = 'bridge'
            """).fetchone()[0]
            bridge_crossings = connection.execute("""
                SELECT COUNT(*) FROM map_edge
                WHERE crossing_type = 'bridge'
                  AND road_class > 0
                  AND river_class > 0
            """).fetchone()[0]
        finally:
            connection.close()

        self.assertEqual(tuple(map_row), (92, 80, 250, 20000, 20000))
        self.assertGreater(cultivated_cells, 0)
        self.assertTrue({1, 2}.issubset(road_classes))
        self.assertTrue({1, 2, 3}.issubset(river_classes))
        self.assertEqual(bridge_paths, 72)
        self.assertEqual(bridge_crossings, 61)

    def test_map_feature_links_must_be_reciprocal(self) -> None:
        connection = manage_database.connect(self.database)
        try:
            cell = connection.execute("""
                SELECT map_id, x, y FROM map_cell
                WHERE road_links > 0 ORDER BY map_id, y, x LIMIT 1
            """).fetchone()
            connection.execute(
                "UPDATE map_cell SET road_links = 0 WHERE map_id = ? AND x = ? AND y = ?",
                tuple(cell),
            )
            connection.commit()
        finally:
            connection.close()
        with self.assertRaisesRegex(manage_database.DataError, "one-way link"):
            manage_database.validate_database(self.database)

    def test_runtime_ids_may_repeat_in_different_scenarios(self) -> None:
        connection = manage_database.connect(self.database)
        try:
            connection.execute("""
                INSERT INTO scenario(
                    scenario_id, scenario_key, display_name, map_id,
                    scenario_year, turn_minutes, cursor_x, cursor_y
                ) VALUES (1, 'second_scenario', 'Second Scenario', 0, 1985, 15, 0, 0)
            """)
            connection.execute("""
                INSERT INTO formation(
                    scenario_id, formation_id, parent_formation_id, faction_id,
                    nation_id, formation_kind_id, formation_key, display_name,
                    command_rating, base_morale
                ) VALUES (1, 0, NULL, 0, 0, 3, 'second_company',
                          'Second Company', 7, 70)
            """)
            connection.execute("""
                INSERT INTO scenario_unit(
                    scenario_id, scenario_unit_id, formation_id, unit_type_id,
                    unit_key, display_name, x, y, strength, morale,
                    suppression, readiness
                ) VALUES (1, 0, 0, 0, 'second_hq', 'Second Headquarters',
                          0, 0, 10, 70, 0, 80)
            """)
            connection.commit()
        finally:
            connection.close()
        counts = manage_database.validate_database(self.database)
        self.assertEqual(counts["scenarios"], 2)
        self.assertEqual(counts["formations"], 6)
        self.assertEqual(counts["scenario_units"], 12)

    def test_exports_are_complete_and_readable(self) -> None:
        output = self.root / "exports"
        manage_database.export_database(self.database, output)
        expected_rows = {
            "unit_types.csv": 64,
            "scenarios.csv": 1,
            "formations.csv": 5,
            "scenario_units.csv": 11,
            "maps.csv": 2,
            "map_sources.csv": 2,
            "terrain_types.csv": 7,
            "map_cells.csv": 7360,
            "map_edges.csv": 2411,
            "map_features.csv": 7443,
        }
        for name, expected in expected_rows.items():
            with (output / name).open(encoding="utf-8", newline="") as source:
                self.assertEqual(len(list(csv.DictReader(source))), expected)

        saved_map = self.root / "saved_map.csv"
        manage_database.save_map_seed(self.database, output=saved_map)
        self.assertEqual(
            saved_map.read_text(encoding="utf-8"),
            manage_database.MAP_CELLS_PATH.read_text(encoding="utf-8"),
        )


if __name__ == "__main__":
    unittest.main()
