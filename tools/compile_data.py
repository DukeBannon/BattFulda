"""Compile Battalion: Fulda source data into compact Amiga assets."""

from __future__ import annotations

import json
from pathlib import Path
import sys

import manage_database


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
OUTPUT = ROOT / "generated"
MAP_WIDTH = 20
MAP_HEIGHT = 15
SIDE_IDS = {"NATO": 0, "WARSAW": 1}
TYPE_IDS = {"armor": 0, "infantry": 1, "recon": 2}


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read {path.relative_to(ROOT)}: {error}") from error


def byte(value: object, field: str, maximum: int = 255) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or not 0 <= value <= maximum:
        raise ValueError(f"{field} must be an integer from 0 to {maximum}")
    return value


def compile_map() -> bytes:
    source = load_json(DATA / "maps" / "fulda_gap.json")
    rows = source.get("rows")
    legend = source.get("legend")
    if not isinstance(rows, list) or len(rows) != MAP_HEIGHT:
        raise ValueError(f"map rows must contain exactly {MAP_HEIGHT} strings")
    if not isinstance(legend, dict):
        raise ValueError("map legend must be an object")

    tiles = bytearray()
    for row_number, row in enumerate(rows):
        if not isinstance(row, str) or len(row) != MAP_WIDTH:
            raise ValueError(f"map row {row_number} must contain exactly {MAP_WIDTH} characters")
        for column, symbol in enumerate(row):
            if symbol not in legend:
                raise ValueError(f"map symbol {symbol!r} at ({column}, {row_number}) is not in the legend")
            tiles.append(byte(legend[symbol], f"legend value for {symbol!r}", 4))
    return bytes((MAP_WIDTH, MAP_HEIGHT)) + tiles


def compile_units() -> bytes:
    source = load_json(DATA / "units" / "d1_units.json")
    units = source.get("units")
    if not isinstance(units, list) or not 1 <= len(units) <= 31:
        raise ValueError("units must contain between 1 and 31 records")

    result = bytearray((len(units),))
    for index, unit in enumerate(units):
        if not isinstance(unit, dict):
            raise ValueError(f"unit {index} must be an object")
        try:
            side = SIDE_IDS[unit.get("side")]
            unit_type = TYPE_IDS[unit.get("type")]
        except KeyError as error:
            raise ValueError(f"unit {index} has an unsupported side or type") from error
        x = byte(unit.get("x"), f"unit {index} x", MAP_WIDTH - 1)
        y = byte(unit.get("y"), f"unit {index} y", MAP_HEIGHT - 1)
        strength = byte(unit.get("strength"), f"unit {index} strength", 15)
        result.extend((side, unit_type, x, y, strength))
    return bytes(result)


def compile_scenario(unit_count: int) -> bytes:
    source = load_json(DATA / "scenarios" / "d1_skirmish.json")
    if source.get("map") != "fulda_gap" or source.get("units") != "d1_units":
        raise ValueError("D1 scenario must reference fulda_gap and d1_units")
    cursor = source.get("cursor")
    if not isinstance(cursor, list) or len(cursor) != 2:
        raise ValueError("scenario cursor must be an [x, y] pair")
    x = byte(cursor[0], "scenario cursor x", MAP_WIDTH - 1)
    y = byte(cursor[1], "scenario cursor y", MAP_HEIGHT - 1)
    return bytes((1, x, y, unit_count))


def main() -> int:
    try:
        map_data = compile_map()
        unit_data = compile_units()
        scenario_data = compile_scenario(unit_data[0])
        OUTPUT.mkdir(parents=True, exist_ok=True)
        outputs = {"map.bin": map_data, "units.bin": unit_data, "scenario.bin": scenario_data}
        for name, contents in outputs.items():
            (OUTPUT / name).write_bytes(contents)
            print(f"Generated {name}: {len(contents)} bytes")
        manage_database.export_database()
        database_outputs = manage_database.compile_database_assets()
        for name, contents in database_outputs.items():
            print(f"Generated {name}: {len(contents)} bytes")
    except ValueError as error:
        print(f"Data error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
