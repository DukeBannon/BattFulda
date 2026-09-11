"""Create, validate, export, and compile the Battalion: Fulda SQLite data."""

from __future__ import annotations

import argparse
from contextlib import closing
import csv
from pathlib import Path
import sqlite3
import struct
import sys
from typing import Iterable, Sequence


ROOT = Path(__file__).resolve().parents[1]
DATABASE_DIRECTORY = ROOT / "data" / "database"
DATABASE_PATH = DATABASE_DIRECTORY / "battfulda.sqlite"
SCHEMA_PATH = DATABASE_DIRECTORY / "schema.sql"
SEED_PATH = DATABASE_DIRECTORY / "seed.sql"
SCENARIO_SEED_PATH = DATABASE_DIRECTORY / "scenario_seed.sql"
UNIT_TYPES_PATH = ROOT / "data" / "units" / "unit_types.csv"
EXPORT_DIRECTORY = DATABASE_DIRECTORY / "exports"
OUTPUT_DIRECTORY = ROOT / "generated"
SCHEMA_VERSION = 1
DEFAULT_SCENARIO = "a2_command_post"


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
        "availability_1985", "notes", "source_url", "rating_status",
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
            recon, command, availability_1985, notes, source_url, rating_status
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                record["availability_1985"], record["notes"], record["source_url"],
                record["rating_status"],
            ),
        )
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
                   s.scenario_year, s.turn_minutes, s.cursor_x, s.cursor_y, s.notes
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
            FROM v_scenario_order_of_battle
            ORDER BY scenario_key, scenario_unit_id
        """)


def encoded_label(value: str, field: str) -> bytes:
    try:
        encoded = value.encode("ascii")
    except UnicodeEncodeError as error:
        raise DataError(f"{field} must use Amiga-safe ASCII characters") from error
    if len(encoded) > 255:
        raise DataError(f"{field} is too long")
    return encoded + b"\0"


def append_string(table: bytearray, value: str, field: str) -> int:
    offset = len(table)
    table.extend(encoded_label(value, field))
    if offset > 65535:
        raise DataError("Compiled string table exceeds 65535 bytes")
    return offset


def compile_unit_types(connection: sqlite3.Connection) -> bytes:
    rows = list(connection.execute("""
        SELECT unit_type_id, type_key, faction_id, nation_id, echelon_id, category_id,
               mobility_id, move_points, hard_attack, soft_attack, defense,
               range_cells, recon, command, display_name
        FROM unit_type ORDER BY unit_type_id
    """))
    strings = bytearray()
    records = bytearray()
    record_format = ">BBBBBBBBBBBBH"
    record_size = struct.calcsize(record_format)
    header_size = struct.calcsize(">4sBBBH")
    for row in rows:
        name_offset = append_string(strings, row["display_name"], row["type_key"])
        records.extend(struct.pack(
            record_format, row["faction_id"], row["nation_id"], row["echelon_id"],
            row["category_id"], row["mobility_id"], row["move_points"],
            row["hard_attack"], row["soft_attack"], row["defense"],
            row["range_cells"], row["recon"], row["command"], name_offset,
        ))
    strings_offset = header_size + len(records)
    return struct.pack(">4sBBBH", b"BFUT", 1, len(rows), record_size, strings_offset) + records + strings


def scenario_row(connection: sqlite3.Connection, scenario_key: str) -> sqlite3.Row:
    row = connection.execute("""
        SELECT s.*, m.width, m.height FROM scenario s
        JOIN map m ON m.map_id = s.map_id WHERE s.scenario_key = ?
    """, (scenario_key,)).fetchone()
    if row is None:
        raise DataError(f"Unknown scenario: {scenario_key}")
    return row


def compile_formations(connection: sqlite3.Connection, scenario_key: str) -> bytes:
    scenario = scenario_row(connection, scenario_key)
    rows = list(connection.execute("""
        SELECT formation_id, parent_formation_id, faction_id, nation_id,
               formation_kind_id, command_rating, base_morale, display_name,
               formation_key
        FROM formation WHERE scenario_id = ? ORDER BY formation_id
    """, (scenario["scenario_id"],)))
    strings = bytearray()
    records = bytearray()
    record_format = ">BBBBBBBH"
    record_size = struct.calcsize(record_format)
    header_size = struct.calcsize(">4sBBBH")
    for row in rows:
        name_offset = append_string(strings, row["display_name"], row["formation_key"])
        parent = 255 if row["parent_formation_id"] is None else row["parent_formation_id"]
        records.extend(struct.pack(
            record_format, row["formation_id"], parent, row["faction_id"],
            row["nation_id"], row["formation_kind_id"], row["command_rating"],
            row["base_morale"], name_offset,
        ))
    strings_offset = header_size + len(records)
    return struct.pack(">4sBBBH", b"BFFM", 1, len(rows), record_size, strings_offset) + records + strings


def compile_scenario(connection: sqlite3.Connection, scenario_key: str) -> bytes:
    scenario = scenario_row(connection, scenario_key)
    units = list(connection.execute("""
        SELECT scenario_unit_id, unit_type_id, formation_id, x, y, strength,
               morale, suppression, readiness
        FROM scenario_unit WHERE scenario_id = ? ORDER BY scenario_unit_id
    """, (scenario["scenario_id"],)))
    formation_count = scalar(
        connection, "SELECT COUNT(*) FROM formation WHERE scenario_id = ?",
        (scenario["scenario_id"],),
    )
    record_format = ">BBBBBBBBB"
    records = b"".join(struct.pack(record_format, *tuple(row)) for row in units)
    header = struct.pack(
        ">4sBBBBBBBBBB", b"BFSC", 1, scenario["scenario_id"], scenario["width"],
        scenario["height"], scenario["turn_minutes"], scenario["cursor_x"],
        scenario["cursor_y"], formation_count, len(units), struct.calcsize(record_format),
    )
    return header + records


def compile_database_assets(
    path: Path = DATABASE_PATH,
    output: Path = OUTPUT_DIRECTORY,
    scenario_key: str = DEFAULT_SCENARIO,
) -> dict[str, bytes]:
    validate_database(path)
    with closing(connect(path)) as connection:
        assets = {
            "unit_types.bin": compile_unit_types(connection),
            "formations.bin": compile_formations(connection, scenario_key),
            "a2_scenario.bin": compile_scenario(connection, scenario_key),
        }
    output.mkdir(parents=True, exist_ok=True)
    for name, contents in assets.items():
        (output / name).write_bytes(contents)
    return assets


def report(counts: dict[str, int]) -> str:
    return ", ".join(f"{value} {name.replace('_', ' ')}" for name, value in counts.items())


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    init_parser = subparsers.add_parser("init", help="create and seed the SQLite database")
    init_parser.add_argument("--force", action="store_true", help="replace an existing database")
    subparsers.add_parser("validate", help="validate relationships and runtime limits")
    subparsers.add_parser("export", help="write deterministic CSV snapshots")
    compile_parser = subparsers.add_parser("compile", help="compile Amiga database assets")
    compile_parser.add_argument("--scenario", default=DEFAULT_SCENARIO)
    args = parser.parse_args(list(argv) if argv is not None else None)

    try:
        if args.command == "init":
            initialize_database(force=args.force)
            counts = validate_database()
            export_database()
            print(f"Created {DATABASE_PATH.relative_to(ROOT)}: {report(counts)}")
        elif args.command == "validate":
            print(f"Database valid: {report(validate_database())}")
        elif args.command == "export":
            export_database()
            print(f"Exported database snapshots to {EXPORT_DIRECTORY.relative_to(ROOT)}")
        elif args.command == "compile":
            assets = compile_database_assets(scenario_key=args.scenario)
            for name, contents in assets.items():
                print(f"Generated {name}: {len(contents)} bytes")
    except (DataError, OSError, sqlite3.Error, struct.error) as error:
        print(f"Database error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
