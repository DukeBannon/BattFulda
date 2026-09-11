"""Aggregate official BKG DGM200 elevations into the 500 m Point Alpha grid."""

from __future__ import annotations

import argparse
from collections import deque
import csv
from contextlib import contextmanager
import json
import math
from pathlib import Path
from typing import Any, Callable, Iterator, TextIO
import zipfile


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "data" / "maps" / "point_alpha_cells.csv"
ARCHIVE_MEMBER = "dgm200.utm32s.gridascii/dgm200/dgm200_utm32s.asc"
MAP_KEY = "point_alpha_corridor"
WIDTH = 40
HEIGHT = 40
CELL_SIZE = 500
ORIGIN_EASTING = 554000
ORIGIN_NORTHING = 5610000
FEATURE_FILES = {
    "roads": "dlm250_roads.geojson",
    "woods": "dlm250_woods.geojson",
    "settlements": "dlm250_settlements.geojson",
    "moor": "dlm250_moor.geojson",
    "swamp": "dlm250_swamp.geojson",
    "rough": "dlm250_rough.geojson",
    "water_axis": "dlm250_water_axis.geojson",
    "standing_water": "dlm250_standing_water.geojson",
    "bridges": "dlm250_bridges.geojson",
}


class MapBuildError(ValueError):
    """A human-readable map source or conversion error."""


@contextmanager
def open_grid(path: Path) -> Iterator[TextIO]:
    if path.suffix.lower() == ".zip":
        archive = zipfile.ZipFile(path)
        try:
            with archive.open(ARCHIVE_MEMBER) as binary:
                import io

                yield io.TextIOWrapper(binary, encoding="ascii")
        finally:
            archive.close()
    else:
        with path.open("r", encoding="ascii") as source:
            yield source


def read_header(source: TextIO) -> dict[str, float]:
    header: dict[str, float] = {}
    for _ in range(6):
        parts = source.readline().split()
        if len(parts) != 2:
            raise MapBuildError("DGM200 ASCII grid has an invalid header")
        header[parts[0].lower()] = float(parts[1])
    required = {"ncols", "nrows", "xllcenter", "yllcenter", "cellsize", "nodata_value"}
    if set(header) != required:
        raise MapBuildError("DGM200 ASCII grid header fields are incomplete")
    if int(header["cellsize"]) != 200:
        raise MapBuildError("Expected the 200 m DGM200 product")
    return header


def aggregate_elevations(source: TextIO) -> list[list[int]]:
    header = read_header(source)
    columns = int(header["ncols"])
    rows = int(header["nrows"])
    source_cell = int(header["cellsize"])
    x_origin = int(header["xllcenter"])
    y_origin = int(header["yllcenter"])
    no_data = header["nodata_value"]
    north_limit = ORIGIN_NORTHING + HEIGHT * CELL_SIZE
    sums = [[0.0 for _ in range(WIDTH)] for _ in range(HEIGHT)]
    counts = [[0 for _ in range(WIDTH)] for _ in range(HEIGHT)]

    first_column = max(0, (ORIGIN_EASTING - x_origin + source_cell - 1) // source_cell)
    last_column = min(
        columns,
        (ORIGIN_EASTING + WIDTH * CELL_SIZE - x_origin + source_cell - 1)
        // source_cell,
    )

    for row_index in range(rows):
        line = source.readline()
        if not line:
            raise MapBuildError("DGM200 ASCII grid ended before its declared row count")
        northing = y_origin + (rows - 1 - row_index) * source_cell
        if northing >= north_limit:
            continue
        if northing < ORIGIN_NORTHING:
            break
        values = line.split()
        if len(values) != columns:
            raise MapBuildError(f"DGM200 row {row_index} has the wrong column count")
        game_y = (north_limit - 1 - northing) // CELL_SIZE
        for column in range(first_column, last_column):
            easting = x_origin + column * source_cell
            if easting < ORIGIN_EASTING or easting >= ORIGIN_EASTING + WIDTH * CELL_SIZE:
                continue
            value = float(values[column])
            if value == no_data:
                continue
            game_x = (easting - ORIGIN_EASTING) // CELL_SIZE
            sums[game_y][game_x] += value
            counts[game_y][game_x] += 1

    result: list[list[int]] = []
    for y in range(HEIGHT):
        result_row: list[int] = []
        for x in range(WIDTH):
            if not counts[y][x]:
                raise MapBuildError(f"No DGM200 elevation samples for game cell ({x}, {y})")
            result_row.append(int(sums[y][x] / counts[y][x] + 0.5))
        result.append(result_row)
    return result


def load_features(directory: Path, key: str) -> list[dict[str, Any]]:
    path = directory / FEATURE_FILES[key]
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise MapBuildError(f"Cannot read {path}: {error}") from error
    features = data.get("features")
    if data.get("type") != "FeatureCollection" or not isinstance(features, list):
        raise MapBuildError(f"{path.name} has no feature list")
    crs_data = data.get("crs") or {}
    crs = crs_data.get("properties", {}).get("name", "")
    if features and not crs.endswith("25832"):
        raise MapBuildError(f"{path.name} is not an EPSG:25832 feature collection")
    return features


def cell_for_point(easting: float, northing: float) -> tuple[int, int] | None:
    x = int((easting - ORIGIN_EASTING) // CELL_SIZE)
    south_y = int((northing - ORIGIN_NORTHING) // CELL_SIZE)
    y = HEIGHT - 1 - south_y
    if 0 <= x < WIDTH and 0 <= y < HEIGHT:
        return x, y
    return None


def line_strings(geometry: dict[str, Any]) -> Iterator[list[list[float]]]:
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if kind == "LineString":
        yield coordinates
    elif kind == "MultiLineString":
        yield from coordinates


def polygons(geometry: dict[str, Any]) -> Iterator[list[list[list[float]]]]:
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if kind == "Polygon":
        yield coordinates
    elif kind == "MultiPolygon":
        yield from coordinates


def point_in_ring(point: tuple[float, float], ring: list[list[float]]) -> bool:
    px, py = point
    inside = False
    previous = ring[-1]
    for current in ring:
        x1, y1 = previous
        x2, y2 = current
        if (y1 > py) != (y2 > py):
            crossing = (x2 - x1) * (py - y1) / (y2 - y1) + x1
            if px < crossing:
                inside = not inside
        previous = current
    return inside


def point_in_polygon(point: tuple[float, float], polygon: list[list[list[float]]]) -> bool:
    return bool(polygon) and point_in_ring(point, polygon[0]) and not any(
        point_in_ring(point, hole) for hole in polygon[1:]
    )


def mark_areas(
    cells: list[list[dict[str, int]]], features: list[dict[str, Any]], field: str
) -> None:
    sample_offsets = ((0.5, 0.5), (0.25, 0.25), (0.75, 0.25),
                      (0.25, 0.75), (0.75, 0.75))
    for feature in features:
        for polygon in polygons(feature.get("geometry") or {}):
            if not polygon or not polygon[0]:
                continue
            eastings = [point[0] for point in polygon[0]]
            northings = [point[1] for point in polygon[0]]
            minimum = cell_for_point(min(eastings), min(northings))
            maximum = cell_for_point(max(eastings), max(northings))
            min_x = 0 if minimum is None else minimum[0]
            max_x = WIDTH - 1 if maximum is None else maximum[0]
            # Game Y runs north-to-south, so derive a safe clipped range directly.
            north_y = HEIGHT - 1 - int((max(northings) - ORIGIN_NORTHING) // CELL_SIZE)
            south_y = HEIGHT - 1 - int((min(northings) - ORIGIN_NORTHING) // CELL_SIZE)
            for y in range(max(0, north_y), min(HEIGHT - 1, south_y) + 1):
                for x in range(max(0, min_x), min(WIDTH - 1, max_x) + 1):
                    hits = 0
                    for offset_x, offset_y in sample_offsets:
                        easting = ORIGIN_EASTING + (x + offset_x) * CELL_SIZE
                        northing = ORIGIN_NORTHING + (HEIGHT - y - offset_y) * CELL_SIZE
                        if point_in_polygon((easting, northing), polygon):
                            hits += 1
                    if hits:
                        cells[y][x][field] = max(cells[y][x][field], hits)


def hex_neighbors(x: int, y: int) -> Iterator[tuple[int, int, int, int]]:
    """Yield odd-column-down hex neighbors as x, y, direction bit, opposite bit."""
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
    for delta_x, delta_y, direction, opposite in directions:
        neighbor_x = x + delta_x
        neighbor_y = y + delta_y
        if 0 <= neighbor_x < WIDTH and 0 <= neighbor_y < HEIGHT:
            yield neighbor_x, neighbor_y, direction, opposite


def shortest_hex_path(
    start: tuple[int, int], end: tuple[int, int]
) -> list[tuple[int, int]]:
    if start == end:
        return [start]
    frontier = deque([start])
    previous: dict[tuple[int, int], tuple[int, int] | None] = {start: None}
    while frontier:
        current = frontier.popleft()
        for neighbor_x, neighbor_y, _, _ in hex_neighbors(*current):
            neighbor = (neighbor_x, neighbor_y)
            if neighbor in previous:
                continue
            previous[neighbor] = current
            if neighbor == end:
                path = [end]
                while path[-1] != start:
                    parent = previous[path[-1]]
                    if parent is None:
                        raise MapBuildError("Hex path ended unexpectedly")
                    path.append(parent)
                path.reverse()
                return path
            frontier.append(neighbor)
    raise MapBuildError(f"No hex path between {start} and {end}")


def connect_hex_cells(
    cells: list[list[dict[str, int]]], start: tuple[int, int],
    end: tuple[int, int], link_field: str, class_field: str, class_value: int,
) -> None:
    path = shortest_hex_path(start, end)
    for x, y in path:
        cells[y][x][class_field] = max(cells[y][x][class_field], class_value)
    for first, second in zip(path, path[1:]):
        for neighbor_x, neighbor_y, direction, opposite in hex_neighbors(*first):
            if (neighbor_x, neighbor_y) != second:
                continue
            cells[first[1]][first[0]][link_field] |= direction
            cells[second[1]][second[0]][link_field] |= opposite
            break


def mark_lines(
    cells: list[list[dict[str, int]]], features: list[dict[str, Any]], field: str,
    classifier: Callable[[dict[str, Any]], int], link_field: str | None = None,
) -> None:
    for feature in features:
        value = classifier(feature.get("properties") or {})
        for line in line_strings(feature.get("geometry") or {}):
            previous: tuple[int, int] | None = None
            for start, end in zip(line, line[1:]):
                delta_e = end[0] - start[0]
                delta_n = end[1] - start[1]
                steps = max(1, math.ceil(max(abs(delta_e), abs(delta_n)) / 100.0))
                for step in range(steps + 1):
                    point = cell_for_point(
                        start[0] + delta_e * step / steps,
                        start[1] + delta_n * step / steps,
                    )
                    if point is None:
                        previous = None
                        continue
                    x, y = point
                    cells[y][x][field] = max(cells[y][x][field], value)
                    if link_field is not None and previous is not None and previous != point:
                        connect_hex_cells(cells, previous, point, link_field, field, value)
                    previous = point


def road_class(properties: dict[str, Any]) -> int:
    designation = str(properties.get("bez") or "").upper()
    if designation.startswith("A"):
        return 3
    if designation.startswith("B"):
        return 2
    return 1


def river_class(properties: dict[str, Any]) -> int:
    width = float(properties.get("brg") or 0)
    if width > 12:
        return 3
    if width > 3:
        return 2
    return 1


def enrich_cells(elevations: list[list[int]], directory: Path) -> list[list[dict[str, int]]]:
    cells = [[{
        "road": 0, "road_links": 0, "river": 0, "river_links": 0,
        "settlement": 0, "bridge": 0,
        "woods": 0, "marsh": 0, "rough": 0, "water": 0,
    } for _ in range(WIDTH)] for _ in range(HEIGHT)]
    mark_lines(cells, load_features(directory, "roads"), "road", road_class, "road_links")
    mark_lines(cells, load_features(directory, "water_axis"), "river", river_class, "river_links")
    mark_lines(cells, load_features(directory, "bridges"), "bridge", lambda _: 1)
    mark_areas(cells, load_features(directory, "woods"), "woods")
    mark_areas(cells, load_features(directory, "settlements"), "settlement")
    mark_areas(cells, load_features(directory, "moor"), "marsh")
    mark_areas(cells, load_features(directory, "swamp"), "marsh")
    mark_areas(cells, load_features(directory, "rough"), "rough")
    mark_areas(cells, load_features(directory, "standing_water"), "water")
    return cells


def write_cells(
    elevations: list[list[int]], output: Path,
    feature_cells: list[list[dict[str, int]]] | None = None,
) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as target:
        writer = csv.writer(target, lineterminator="\n")
        writer.writerow((
            "map_key", "x", "y", "elevation_m", "terrain_key", "road_class",
            "road_links", "river_class", "river_links", "settlement_level",
            "has_bridge", "source_status", "notes",
        ))
        for y, row in enumerate(elevations):
            for x, elevation in enumerate(row):
                features = feature_cells[y][x] if feature_cells else {}
                if features.get("water"):
                    terrain = "water"
                elif features.get("settlement"):
                    terrain = "urban"
                elif features.get("marsh"):
                    terrain = "marsh"
                elif features.get("woods"):
                    terrain = "woods"
                elif features.get("rough"):
                    terrain = "rough"
                else:
                    terrain = "clear"
                has_features = any(features.values())
                writer.writerow((
                    MAP_KEY, x, y, elevation, terrain,
                    features.get("road", 0), features.get("road_links", 0),
                    features.get("river", 0), features.get("river_links", 0),
                    min(3, features.get("settlement", 0)),
                    1 if features.get("bridge") else 0,
                    "draft" if has_features else "elevation_only", "",
                ))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dgm200", type=Path, help="BKG DGM200 GRID-ASCII ZIP or ASC file")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--dlm250-dir", type=Path,
        help="Directory containing the clipped BKG DLM250 GeoJSON layer files",
    )
    arguments = parser.parse_args()
    try:
        with open_grid(arguments.dgm200) as source:
            elevations = aggregate_elevations(source)
        feature_cells = (
            enrich_cells(elevations, arguments.dlm250_dir)
            if arguments.dlm250_dir else None
        )
        write_cells(elevations, arguments.output, feature_cells)
    except (OSError, zipfile.BadZipFile, KeyError, MapBuildError) as error:
        parser.error(str(error))
    values = [value for row in elevations for value in row]
    print(
        f"Generated {arguments.output}: {WIDTH}x{HEIGHT} cells, "
        f"elevation {min(values)}..{max(values)} m"
        + (", DLM250 feature layers applied" if arguments.dlm250_dir else "")
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
