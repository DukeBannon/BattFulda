"""Create, validate, and export the Battalion: Fulda SQLite data."""

from __future__ import annotations

import argparse
from contextlib import closing
import csv
from pathlib import Path
import sqlite3
import sys
from typing import Iterable, Sequence


ROOT = Path(__file__).resolve().parents[1]
DATABASE_DIRECTORY = ROOT / "data" / "database"
DATABASE_PATH = DATABASE_DIRECTORY / "battfulda.sqlite"
SCHEMA_PATH = DATABASE_DIRECTORY / "schema.sql"
SEED_PATH = DATABASE_DIRECTORY / "seed.sql"
SCENARIO_SEED_PATH = DATABASE_DIRECTORY / "scenario_seed.sql"
MAP_SEED_PATH = DATABASE_DIRECTORY / "map_seed.sql"
UNIT_TYPES_PATH = ROOT / "data" / "units" / "unit_types.csv"
MAP_CELLS_PATH = ROOT / "data" / "maps" / "point_alpha_cells.csv"
MAP_EDGES_PATH = ROOT / "data" / "maps" / "point_alpha_edges.csv"
MAP_FEATURES_PATH = ROOT / "data" / "maps" / "point_alpha_features.csv"
MAP_CROSSINGS_PATH = ROOT / "data" / "maps" / "point_alpha_crossings.csv"
EXPORT_DIRECTORY = DATABASE_DIRECTORY / "exports"
SCHEMA_VERSION = 8
DEFAULT_SCENARIO = "a2_command_post"
DEFAULT_PRODUCTION_MAP = "point_alpha_corridor"


class DataError(ValueError):
    """A human-readable database or source-data error."""


def connect(path: Path = DATABASE_PATH) -> sqlite3.Connection:
    connection = sqlite3.connect(path)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    return connection


def read_sql(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error


def lookup(
    connection: sqlite3.Connection, table: str, id_column: str, key_column: str
) -> dict[str, int]:
    return {
        row["display_name"]: row[0]
        for row in connection.execute(
            f"SELECT {id_column}, display_name FROM {table} ORDER BY {id_column}"
        )
    } | {
        row[key_column]: row[0]
        for row in connection.execute(
            f"SELECT {id_column}, {key_column} FROM {table} ORDER BY {id_column}"
        )
    }


def integer(record: dict[str, str], field: str, minimum: int, maximum: int) -> int:
    try:
        value = int(record[field])
    except (KeyError, ValueError) as error:
        raise DataError(f"{record.get('type_id', 'unit type')}: invalid {field}") from error
    if not minimum <= value <= maximum:
        raise DataError(
            f"{record.get('type_id', 'unit type')}: {field} must be {minimum}..{maximum}"
        )
    return value


def validate_feature_links(connection: sqlite3.Connection) -> None:
    cells = {
        (row["map_id"], row["x"], row["y"]): row
        for row in connection.execute(
            "SELECT map_id, x, y, road_links, river_links FROM map_cell"
        )
    }
    for (map_id, x, y), row in cells.items():
        diagonal_up = -1 if x % 2 == 0 else 0
        diagonal_down = 0 if x % 2 == 0 else 1
        directions = (
            (0, -1, 1, 8),
            (1, diagonal_up, 2, 16),
            (1, diagonal_down, 4, 32),
            (0, 1, 8, 1),
            (-1, diagonal_down, 16, 2),
            (-1, diagonal_up, 32, 4),
        )
        for field in ("road_links", "river_links"):
            mask = row[field]
            for delta_x, delta_y, direction, opposite in directions:
                if not mask & direction:
                    continue
                neighbor = cells.get((map_id, x + delta_x, y + delta_y))
                if neighbor is None or not neighbor[field] & opposite:
                    raise DataError(
                        f"{field} has a one-way link at map {map_id} cell ({x}, {y})"
                    )


def import_unit_types(connection: sqlite3.Connection, path: Path = UNIT_TYPES_PATH) -> int:
    try:
        source = path.open("r", encoding="utf-8", newline="")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error

    faction_ids = lookup(connection, "faction", "faction_id", "faction_key")
    nation_ids = lookup(connection, "nation", "nation_id", "nation_key")
    echelon_ids = lookup(connection, "echelon", "echelon_id", "echelon_key")
    category_ids = lookup(connection, "unit_category", "category_id", "category_key")
    mobility_ids = lookup(connection, "mobility_class", "mobility_id", "mobility_key")
    expected_fields = {
        "type_id", "faction", "country", "echelon", "category", "display_name",
        "equipment", "role", "mobility", "move_points", "hard_attack",
        "soft_attack", "defense", "range_cells", "recon", "command",
        "amphibious", "availability_1985", "notes", "source_url", "rating_status",
    }

    with source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or set(reader.fieldnames) != expected_fields:
            raise DataError("unit_types.csv columns do not match the database import schema")
        records = list(reader)

    if not records or len(records) > 255:
        raise DataError("unit_types.csv must contain 1..255 unit types")
    keys = [record["type_id"] for record in records]
    if len(keys) != len(set(keys)):
        raise DataError("unit_types.csv contains duplicate type_id values")

    insert_sql = """
        INSERT INTO unit_type(
            unit_type_id, type_key, faction_id, nation_id, echelon_id,
            category_id, mobility_id, display_name, equipment, role,
            move_points, hard_attack, soft_attack, defense, range_cells,
            recon, command, amphibious, availability_1985, notes, source_url, rating_status
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    for runtime_id, record in enumerate(records):
        try:
            faction_id = faction_ids[record["faction"]]
            nation_id = nation_ids[record["country"]]
            echelon_id = echelon_ids[record["echelon"]]
            category_id = category_ids[record["category"]]
            mobility_id = mobility_ids[record["mobility"]]
        except KeyError as error:
            raise DataError(
                f"{record['type_id']}: unknown faction, nation, echelon, category, or mobility"
            ) from error
        nation_faction = connection.execute(
            "SELECT faction_id FROM nation WHERE nation_id = ?", (nation_id,)
        ).fetchone()[0]
        if nation_faction != faction_id:
            raise DataError(f"{record['type_id']}: nation does not belong to faction")
        connection.execute(
            insert_sql,
            (
                runtime_id, record["type_id"], faction_id, nation_id, echelon_id,
                category_id, mobility_id, record["display_name"], record["equipment"],
                record["role"], integer(record, "move_points", 0, 15),
                integer(record, "hard_attack", 0, 15),
                integer(record, "soft_attack", 0, 15),
                integer(record, "defense", 0, 15),
                integer(record, "range_cells", 0, 255),
                integer(record, "recon", 0, 15), integer(record, "command", 0, 15),
                integer(record, "amphibious", 0, 1), record["availability_1985"],
                record["notes"], record["source_url"],
                record["rating_status"],
            ),
        )
    return len(records)


def import_map_cells(connection: sqlite3.Connection, path: Path = MAP_CELLS_PATH) -> int:
    try:
        source = path.open("r", encoding="utf-8", newline="")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error
    expected_fields = {
        "map_key", "x", "y", "elevation_m", "terrain_key", "road_class",
        "road_links", "river_class", "river_links", "settlement_level",
        "has_bridge", "source_status", "notes",
    }
    map_ids = lookup(connection, "map", "map_id", "map_key")
    terrain_ids = lookup(connection, "terrain_type", "terrain_id", "terrain_key")
    with source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or set(reader.fieldnames) != expected_fields:
            raise DataError("point_alpha_cells.csv columns do not match the map import schema")
        records = list(reader)
    insert_sql = """
        INSERT INTO map_cell(
            map_id, x, y, terrain_id, elevation_m, road_class, road_links,
            river_class, river_links, settlement_level, has_bridge,
            source_status, notes
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    for record in records:
        try:
            map_id = map_ids[record["map_key"]]
            terrain_id = terrain_ids[record["terrain_key"]]
            values = (
                map_id, int(record["x"]), int(record["y"]), terrain_id,
                int(record["elevation_m"]), int(record["road_class"]),
                int(record["road_links"]), int(record["river_class"]),
                int(record["river_links"]), int(record["settlement_level"]),
                int(record["has_bridge"]), record["source_status"], record["notes"],
            )
        except (KeyError, ValueError) as error:
            raise DataError(f"Invalid map cell record: {record}") from error
        try:
            connection.execute(insert_sql, values)
        except sqlite3.IntegrityError as error:
            raise DataError(
                f"Invalid or duplicate map cell ({record.get('x')}, {record.get('y')}): {error}"
            ) from error
    return len(records)


def import_map_edges(connection: sqlite3.Connection, path: Path = MAP_EDGES_PATH) -> int:
    expected_fields = {
        "map_key", "x", "y", "direction", "neighbor_x", "neighbor_y",
        "road_class", "river_class", "crossing_type", "source_status",
    }
    map_ids = lookup(connection, "map", "map_id", "map_key")
    try:
        source = path.open("r", encoding="utf-8", newline="")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error
    with source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or set(reader.fieldnames) != expected_fields:
            raise DataError("point_alpha_edges.csv columns do not match the map-edge schema")
        records = list(reader)
    insert_sql = """
        INSERT INTO map_edge(
            map_id, x, y, direction, neighbor_x, neighbor_y, road_class,
            river_class, crossing_type, source_status
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    for record in records:
        try:
            connection.execute(insert_sql, (
                map_ids[record["map_key"]], int(record["x"]), int(record["y"]),
                int(record["direction"]), int(record["neighbor_x"]),
                int(record["neighbor_y"]), int(record["road_class"]),
                int(record["river_class"]), record["crossing_type"],
                record["source_status"],
            ))
        except (KeyError, ValueError, sqlite3.IntegrityError) as error:
            raise DataError(f"Invalid map edge record: {record}: {error}") from error
    return len(records)


def import_map_features(connection: sqlite3.Connection, path: Path = MAP_FEATURES_PATH) -> int:
    expected_fields = {
        "map_key", "feature_id", "feature_type", "feature_class", "sequence",
        "easting_m", "northing_m", "source_status",
    }
    map_ids = lookup(connection, "map", "map_id", "map_key")
    try:
        source = path.open("r", encoding="utf-8", newline="")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error
    with source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or set(reader.fieldnames) != expected_fields:
            raise DataError("point_alpha_features.csv columns do not match the feature schema")
        records = list(reader)
    seen: set[tuple[int, int]] = set()
    for record in records:
        try:
            map_id = map_ids[record["map_key"]]
            feature_id = int(record["feature_id"])
            key = (map_id, feature_id)
            if key not in seen:
                connection.execute(
                    "INSERT INTO map_feature VALUES (?, ?, ?, ?, ?)",
                    (map_id, feature_id, record["feature_type"],
                     int(record["feature_class"]), record["source_status"]),
                )
                seen.add(key)
            connection.execute(
                "INSERT INTO map_feature_vertex VALUES (?, ?, ?, ?, ?)",
                (map_id, feature_id, int(record["sequence"]),
                 float(record["easting_m"]), float(record["northing_m"])),
            )
        except (KeyError, ValueError, sqlite3.IntegrityError) as error:
            raise DataError(f"Invalid map feature record: {record}: {error}") from error
    return len(seen)


def import_map_crossings(connection: sqlite3.Connection, path: Path = MAP_CROSSINGS_PATH) -> int:
    expected_fields = {
        "map_key", "x", "y", "neighbor_x", "neighbor_y", "river_class",
        "crossing_type", "source_status",
    }
    map_ids = lookup(connection, "map", "map_id", "map_key")
    try:
        source = path.open("r", encoding="utf-8", newline="")
    except OSError as error:
        raise DataError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error
    with source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or set(reader.fieldnames) != expected_fields:
            raise DataError("point_alpha_crossings.csv columns do not match the crossing-edge schema")
        records = list(reader)
    for record in records:
        try:
            connection.execute("""
                INSERT INTO map_crossing_edge(
                    map_id, x, y, neighbor_x, neighbor_y, river_class,
                    crossing_type, source_status
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                map_ids[record["map_key"]], int(record["x"]), int(record["y"]),
                int(record["neighbor_x"]), int(record["neighbor_y"]),
                int(record["river_class"]), record["crossing_type"],
                record["source_status"],
            ))
        except (KeyError, ValueError, sqlite3.IntegrityError) as error:
            raise DataError(f"Invalid movement crossing record: {record}: {error}") from error
    return len(records)


def initialize_database(path: Path = DATABASE_PATH, force: bool = False) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and not force:
        raise DataError(f"Database already exists: {path.relative_to(ROOT)}")
    temporary = path.with_suffix(path.suffix + ".new")
    temporary.unlink(missing_ok=True)
    try:
        connection = connect(temporary)
        try:
            connection.executescript(read_sql(SCHEMA_PATH))
            connection.executescript(read_sql(SEED_PATH))
            import_unit_types(connection)
            connection.executescript(read_sql(MAP_SEED_PATH))
            import_map_cells(connection)
            import_map_edges(connection)
            import_map_features(connection)
            import_map_crossings(connection)
            connection.executescript(read_sql(SCENARIO_SEED_PATH))
            connection.commit()
        finally:
            connection.close()
        validate_database(temporary)
        temporary.replace(path)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise


def scalar(connection: sqlite3.Connection, sql: str, parameters: Sequence[object] = ()) -> int:
    return int(connection.execute(sql, parameters).fetchone()[0])


def validate_database(path: Path = DATABASE_PATH) -> dict[str, int]:
    if not path.is_file():
        raise DataError(f"Database not found: {path.relative_to(ROOT)}")
    with closing(connect(path)) as connection:
        integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
        if integrity != "ok":
            raise DataError(f"SQLite integrity check failed: {integrity}")
        foreign_keys = list(connection.execute("PRAGMA foreign_key_check"))
        if foreign_keys:
            raise DataError(f"SQLite foreign-key check found {len(foreign_keys)} errors")
        version = scalar(connection, "SELECT version FROM schema_info")
        if version != SCHEMA_VERSION:
            raise DataError(f"Expected schema version {SCHEMA_VERSION}, found {version}")

        unit_type_count = scalar(connection, "SELECT COUNT(*) FROM unit_type")
        formation_count = scalar(connection, "SELECT COUNT(*) FROM formation")
        unit_count = scalar(connection, "SELECT COUNT(*) FROM scenario_unit")
        scenario_count = scalar(connection, "SELECT COUNT(*) FROM scenario")
        map_count = scalar(connection, "SELECT COUNT(*) FROM map")
        map_cell_count = scalar(connection, "SELECT COUNT(*) FROM map_cell")
        map_edge_count = scalar(connection, "SELECT COUNT(*) FROM map_edge")
        map_feature_count = scalar(connection, "SELECT COUNT(*) FROM map_feature")
        map_crossing_count = scalar(connection, "SELECT COUNT(*) FROM map_crossing_edge")
        terrain_cost_count = scalar(connection, "SELECT COUNT(*) FROM terrain_movement_cost")
        road_cost_count = scalar(connection, "SELECT COUNT(*) FROM road_movement_cost")
        crossing_cost_count = scalar(connection, "SELECT COUNT(*) FROM water_crossing_cost")
        mobility_count = scalar(connection, "SELECT COUNT(*) FROM mobility_class")
        terrain_type_count = scalar(connection, "SELECT COUNT(*) FROM terrain_type")
        if terrain_cost_count != mobility_count * terrain_type_count:
            raise DataError("Movement data does not cover every mobility and terrain combination")
        if road_cost_count != mobility_count * 3:
            raise DataError("Movement data does not cover every mobility and road class")
        if crossing_cost_count != mobility_count * 9:
            raise DataError("Movement data does not cover every mobility and water crossing")
        if not unit_type_count or not scenario_count:
            raise DataError("Database requires unit types and at least one scenario")
        runtime_ids = [row[0] for row in connection.execute(
            "SELECT unit_type_id FROM unit_type ORDER BY unit_type_id"
        )]
        if runtime_ids != list(range(unit_type_count)):
            raise DataError("unit_type_id values must be contiguous from zero")

        bad_unit_types = scalar(connection, """
            SELECT COUNT(*) FROM unit_type ut
            JOIN nation n ON n.nation_id = ut.nation_id
            WHERE n.faction_id <> ut.faction_id
        """)
        bad_formations = scalar(connection, """
            SELECT COUNT(*) FROM formation child
            LEFT JOIN formation parent
              ON parent.scenario_id = child.scenario_id
             AND parent.formation_id = child.parent_formation_id
            JOIN nation n ON n.nation_id = child.nation_id
            WHERE n.faction_id <> child.faction_id
               OR (child.parent_formation_id IS NOT NULL
                   AND parent.scenario_id <> child.scenario_id)
        """)
        bad_units = scalar(connection, """
            SELECT COUNT(*) FROM scenario_unit su
            JOIN formation f
              ON f.scenario_id = su.scenario_id
             AND f.formation_id = su.formation_id
            JOIN scenario s ON s.scenario_id = su.scenario_id
            JOIN map m ON m.map_id = s.map_id
            WHERE f.scenario_id <> su.scenario_id
               OR su.x >= m.width OR su.y >= m.height
        """)
        bad_scenarios = scalar(connection, """
            SELECT COUNT(*) FROM scenario s JOIN map m ON m.map_id = s.map_id
            WHERE s.cursor_x >= m.width OR s.cursor_y >= m.height
        """)
        if bad_unit_types or bad_formations or bad_units or bad_scenarios:
            raise DataError("Database contains inconsistent faction, scenario, or map relationships")

        bad_map_cells = scalar(connection, """
            SELECT COUNT(*) FROM map_cell mc JOIN map m ON m.map_id = mc.map_id
            WHERE mc.x >= m.width OR mc.y >= m.height
               OR (mc.road_links <> 0 AND mc.road_class = 0)
               OR (mc.river_links <> 0 AND mc.river_class = 0)
        """)
        incomplete_maps = scalar(connection, """
            SELECT COUNT(*) FROM map m
            WHERE m.geographic_status <> 'abstract'
              AND (SELECT COUNT(*) FROM map_cell mc WHERE mc.map_id = m.map_id)
                  <> m.width * m.height
        """)
        unsourced_maps = scalar(connection, """
            SELECT COUNT(*) FROM map m
            WHERE m.geographic_status <> 'abstract'
              AND NOT EXISTS (SELECT 1 FROM map_source ms WHERE ms.map_id = m.map_id)
        """)
        if bad_map_cells or incomplete_maps or unsourced_maps:
            raise DataError("Database contains incomplete, out-of-bounds, or unsourced map data")

        bad_edges = scalar(connection, """
            SELECT COUNT(*) FROM map_edge me JOIN map m ON m.map_id = me.map_id
            WHERE me.x >= m.width OR me.y >= m.height
               OR me.neighbor_x >= m.width OR me.neighbor_y >= m.height
               OR (me.road_class = 0 AND me.river_class = 0)
               OR (me.crossing_type = 'bridge' AND
                   (me.road_class = 0 OR me.river_class = 0))
        """)
        orphan_vertices = scalar(connection, """
            SELECT COUNT(*) FROM map_feature_vertex v
            LEFT JOIN map_feature f
              ON f.map_id = v.map_id AND f.feature_id = v.feature_id
            WHERE f.feature_id IS NULL
        """)
        short_features = scalar(connection, """
            SELECT COUNT(*) FROM map_feature f
            WHERE (SELECT COUNT(*) FROM map_feature_vertex v
                   WHERE v.map_id = f.map_id AND v.feature_id = f.feature_id) < 2
        """)
        if bad_edges or orphan_vertices or short_features:
            raise DataError("Database contains invalid map topology or feature geometry")

        validate_feature_links(connection)

        for formation in connection.execute(
            "SELECT scenario_id, formation_id, parent_formation_id FROM formation"
        ):
            seen: set[int] = set()
            current = formation["formation_id"]
            parent = formation["parent_formation_id"]
            while parent is not None:
                if parent == current or parent in seen:
                    raise DataError(f"Formation hierarchy contains a cycle at {current}")
                seen.add(parent)
                row = connection.execute(
                    """SELECT parent_formation_id FROM formation
                       WHERE scenario_id = ? AND formation_id = ?""",
                    (formation["scenario_id"], parent),
                ).fetchone()
                parent = row[0] if row else None

    return {
        "unit_types": unit_type_count,
        "scenarios": scenario_count,
        "formations": formation_count,
        "scenario_units": unit_count,
        "maps": map_count,
        "map_cells": map_cell_count,
        "map_edges": map_edge_count,
        "map_features": map_feature_count,
        "map_crossings": map_crossing_count,
        "terrain_costs": terrain_cost_count,
        "road_costs": road_cost_count,
        "crossing_costs": crossing_cost_count,
    }


def write_query_csv(
    connection: sqlite3.Connection, path: Path, query: str, parameters: Sequence[object] = ()
) -> None:
    rows = connection.execute(query, parameters)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as output:
        writer = csv.writer(output, lineterminator="\n")
        writer.writerow(column[0] for column in rows.description)
        writer.writerows(tuple(row) for row in rows)


def export_database(path: Path = DATABASE_PATH, output: Path = EXPORT_DIRECTORY) -> None:
    validate_database(path)
    with closing(connect(path)) as connection:
        write_query_csv(connection, output / "unit_types.csv", "SELECT * FROM v_unit_type ORDER BY unit_type_id")
        write_query_csv(connection, output / "scenarios.csv", """
            SELECT s.scenario_id, s.scenario_key, s.display_name, m.map_key,
                   s.scenario_year, s.start_datetime, s.turn_minutes,
                   s.cursor_x, s.cursor_y, s.notes
            FROM scenario s JOIN map m ON m.map_id = s.map_id
            ORDER BY s.scenario_id
        """)
        write_query_csv(connection, output / "formations.csv", """
            SELECT f.formation_id, s.scenario_key, f.formation_key,
                   f.display_name, parent.formation_key AS parent_formation,
                   fa.display_name AS faction, n.display_name AS country,
                   fk.formation_kind_key AS formation_kind, f.command_rating,
                   f.base_morale, f.sort_order
            FROM formation f
            JOIN scenario s ON s.scenario_id = f.scenario_id
            LEFT JOIN formation parent
              ON parent.scenario_id = f.scenario_id
             AND parent.formation_id = f.parent_formation_id
            JOIN faction fa ON fa.faction_id = f.faction_id
            JOIN nation n ON n.nation_id = f.nation_id
            JOIN formation_kind fk ON fk.formation_kind_id = f.formation_kind_id
            ORDER BY s.scenario_id, f.sort_order, f.formation_id
        """)
        write_query_csv(connection, output / "scenario_units.csv", """
            SELECT scenario_key, scenario_unit_id, unit_key, unit_name,
                   formation_name, parent_formation, type_key, unit_type,
                   faction, country, x, y, strength, morale, suppression, readiness
                   , amphibious, mobility, move_points
            FROM v_scenario_order_of_battle
            ORDER BY scenario_key, scenario_unit_id
        """)
        write_query_csv(connection, output / "maps.csv", """
            SELECT map_id, map_key, display_name, width, height, cell_size_m,
                   crs, origin_easting_m, origin_northing_m, extent_width_m,
                   extent_height_m, geographic_status, source_path
            FROM map ORDER BY map_id
        """)
        write_query_csv(connection, output / "map_sources.csv", """
            SELECT m.map_key, ms.source_key, ms.display_name, ms.source_url,
                   ms.license_name, ms.attribution, ms.source_date, ms.notes
            FROM map_source ms JOIN map m ON m.map_id = ms.map_id
            ORDER BY m.map_id, ms.map_source_id
        """)
        write_query_csv(connection, output / "terrain_types.csv",
                        "SELECT * FROM terrain_type ORDER BY terrain_id")
        write_query_csv(connection, output / "terrain_movement_costs.csv", """
            SELECT m.mobility_key, t.terrain_key, c.passable,
                   c.quick_cost, c.tactical_cost, c.hunt_cost
            FROM terrain_movement_cost c
            JOIN mobility_class m ON m.mobility_id = c.mobility_id
            JOIN terrain_type t ON t.terrain_id = c.terrain_id
            ORDER BY m.mobility_id, t.terrain_id
        """)
        write_query_csv(connection, output / "road_movement_costs.csv", """
            SELECT m.mobility_key, c.road_class, c.quick_cost,
                   c.tactical_cost, c.hunt_cost
            FROM road_movement_cost c
            JOIN mobility_class m ON m.mobility_id = c.mobility_id
            ORDER BY m.mobility_id, c.road_class
        """)
        write_query_csv(connection, output / "water_crossing_costs.csv", """
            SELECT m.mobility_key, c.river_class, c.crossing_type,
                   c.requires_amphibious, c.quick_cost, c.tactical_cost, c.hunt_cost
            FROM water_crossing_cost c
            JOIN mobility_class m ON m.mobility_id = c.mobility_id
            ORDER BY m.mobility_id, c.river_class, c.crossing_type
        """)
        write_query_csv(connection, output / "movement_parameters.csv",
                        "SELECT * FROM movement_parameter ORDER BY parameter_key")
        write_query_csv(connection, output / "map_cells.csv", """
            SELECT m.map_key, mc.x, mc.y, tt.terrain_key, mc.elevation_m,
                   mc.road_class, mc.road_links, mc.river_class, mc.river_links,
                   mc.settlement_level, mc.has_bridge, mc.source_status, mc.notes
            FROM map_cell mc JOIN map m ON m.map_id = mc.map_id
            JOIN terrain_type tt ON tt.terrain_id = mc.terrain_id
            ORDER BY m.map_id, mc.y, mc.x
        """)
        write_query_csv(connection, output / "map_edges.csv", """
            SELECT m.map_key, me.x, me.y, me.direction, me.neighbor_x,
                   me.neighbor_y, me.road_class, me.river_class,
                   me.crossing_type, me.source_status
            FROM map_edge me JOIN map m ON m.map_id = me.map_id
            ORDER BY m.map_id, me.y, me.x, me.direction
        """)
        write_query_csv(connection, output / "map_crossings.csv", """
            SELECT m.map_key, e.x, e.y, e.neighbor_x, e.neighbor_y,
                   e.river_class, e.crossing_type, e.source_status
            FROM map_crossing_edge e JOIN map m ON m.map_id = e.map_id
            ORDER BY m.map_id, e.y, e.x, e.neighbor_y, e.neighbor_x
        """)
        write_query_csv(connection, output / "map_features.csv", """
            SELECT m.map_key, f.feature_id, f.feature_type, f.feature_class,
                   v.sequence, v.easting_m, v.northing_m, f.source_status
            FROM map_feature f JOIN map m ON m.map_id = f.map_id
            JOIN map_feature_vertex v
              ON v.map_id = f.map_id AND v.feature_id = f.feature_id
            ORDER BY m.map_id, f.feature_id, v.sequence
        """)


def save_map_seed(
    path: Path = DATABASE_PATH,
    map_key: str = DEFAULT_PRODUCTION_MAP,
    output: Path = MAP_CELLS_PATH,
) -> None:
    validate_database(path)
    with closing(connect(path)) as connection:
        exists = connection.execute(
            "SELECT 1 FROM map WHERE map_key = ?", (map_key,)
        ).fetchone()
        if not exists:
            raise DataError(f"Unknown map: {map_key}")
        write_query_csv(connection, output, """
            SELECT m.map_key, mc.x, mc.y, mc.elevation_m, tt.terrain_key,
                   mc.road_class, mc.road_links, mc.river_class, mc.river_links,
                   mc.settlement_level, mc.has_bridge, mc.source_status, mc.notes
            FROM map_cell mc JOIN map m ON m.map_id = mc.map_id
            JOIN terrain_type tt ON tt.terrain_id = mc.terrain_id
            WHERE m.map_key = ? ORDER BY mc.y, mc.x
        """, (map_key,))


def report(counts: dict[str, int]) -> str:
    return ", ".join(f"{value} {name.replace('_', ' ')}" for name, value in counts.items())


def sync_researched_ranges(path: Path = DATABASE_PATH) -> None:
    """Apply documented M1/TOW/T-64 corrections without rebuilding user map data."""
    keys = {"us_tank_platoon_m1", "us_atgm_team_tow", "su_tank_platoon_t64b"}
    with UNIT_TYPES_PATH.open(encoding="utf-8", newline="") as source:
        records = [row for row in csv.DictReader(source) if row["type_id"] in keys]
    if {row["type_id"] for row in records} != keys or len(records) != len(keys):
        raise DataError("Researched range records missing or duplicated")
    with closing(connect(path)) as connection, connection:
        for row in records:
            changed = connection.execute(
                "UPDATE unit_type SET range_cells=?, equipment=?, notes=?, source_url=? WHERE type_key=?",
                (integer(row, "range_cells", 0, 255), row["equipment"], row["notes"],
                 row["source_url"], row["type_id"]),
            ).rowcount
            if changed != 1:
                raise DataError(f"Missing unit type: {row['type_id']}")


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    init_parser = subparsers.add_parser("init", help="create and seed the SQLite database")
    init_parser.add_argument("--force", action="store_true", help="replace an existing database")
    subparsers.add_parser("validate", help="validate relationships and runtime limits")
    subparsers.add_parser("export", help="write deterministic CSV snapshots")
    subparsers.add_parser("sync-ranges", help="apply researched range corrections without rebuilding map data")
    save_map_parser = subparsers.add_parser(
        "save-map", help="save SQLiteStudio map edits to the reproducible map CSV"
    )
    save_map_parser.add_argument("--map", default=DEFAULT_PRODUCTION_MAP)
    save_map_parser.add_argument("--output", type=Path, default=MAP_CELLS_PATH)
    args = parser.parse_args(list(argv) if argv is not None else None)

    try:
        if args.command == "init":
            initialize_database(force=args.force)
            counts = validate_database()
            export_database()
            print(f"Created {DATABASE_PATH.relative_to(ROOT)}: {report(counts)}")
        elif args.command == "sync-ranges":
            sync_researched_ranges()
            counts = validate_database()
            export_database()
            print(f"Applied researched ranges: {report(counts)}")
        elif args.command == "validate":
            print(f"Database valid: {report(validate_database())}")
        elif args.command == "export":
            export_database()
            print(f"Exported database snapshots to {EXPORT_DIRECTORY.relative_to(ROOT)}")
        elif args.command == "save-map":
            save_map_seed(map_key=args.map, output=args.output)
            print(f"Saved map seed to {args.output}")
    except (DataError, OSError, sqlite3.Error) as error:
        print(f"Database error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
