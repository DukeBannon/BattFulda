"""Build the 250 m Point Alpha hex map from official BKG source data."""

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
DEFAULT_EDGE_OUTPUT = ROOT / "data" / "maps" / "point_alpha_edges.csv"
DEFAULT_FEATURE_OUTPUT = ROOT / "data" / "maps" / "point_alpha_features.csv"
ARCHIVE_MEMBER = "dgm200.utm32s.gridascii/dgm200/dgm200_utm32s.asc"
MAP_KEY = "point_alpha_corridor"
WIDTH = 92
HEIGHT = 80
CELL_SIZE = 250
MAP_EXTENT_M = 20_000
HEX_RADIUS_M = CELL_SIZE / math.sqrt(3.0)
HEX_COLUMN_STEP_M = HEX_RADIUS_M * 1.5
ORIGIN_EASTING = 554000
ORIGIN_NORTHING = 5610000
FEATURE_FILES = {
    "roads": "dlm250_roads.geojson",
    "cultivated": "dlm250_cultivated.geojson",
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
    north_limit = ORIGIN_NORTHING + MAP_EXTENT_M
    sums = [[0.0 for _ in range(WIDTH)] for _ in range(HEIGHT)]
    counts = [[0 for _ in range(WIDTH)] for _ in range(HEIGHT)]

    first_column = max(0, (ORIGIN_EASTING - x_origin + source_cell - 1) // source_cell)
    last_column = min(
        columns,
        (ORIGIN_EASTING + MAP_EXTENT_M - x_origin + source_cell - 1)
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
        for column in range(first_column, last_column):
            easting = x_origin + column * source_cell
            if easting < ORIGIN_EASTING or easting >= ORIGIN_EASTING + MAP_EXTENT_M:
                continue
            value = float(values[column])
            if value == no_data:
                continue
            cell = cell_for_point(easting, northing)
            if cell is not None:
                game_x, game_y = cell
                sums[game_y][game_x] += value
                counts[game_y][game_x] += 1

    result: list[list[int]] = []
    for y in range(HEIGHT):
        result_row: list[int] = []
        for x in range(WIDTH):
            if counts[y][x]:
                result_row.append(int(sums[y][x] / counts[y][x] + 0.5))
                continue
            nearby = []
            for radius in range(1, 4):
                for sample_y in range(max(0, y - radius), min(HEIGHT, y + radius + 1)):
                    for sample_x in range(max(0, x - radius), min(WIDTH, x + radius + 1)):
                        if counts[sample_y][sample_x]:
                            nearby.append(sums[sample_y][sample_x] / counts[sample_y][sample_x])
                if nearby:
                    break
            if not nearby:
                raise MapBuildError(f"No DGM200 elevation samples near game cell ({x}, {y})")
            result_row.append(int(sum(nearby) / len(nearby) + 0.5))
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
    local_easting = easting - ORIGIN_EASTING
    local_south = ORIGIN_NORTHING + MAP_EXTENT_M - northing
    x = round((local_easting - HEX_RADIUS_M) / HEX_COLUMN_STEP_M)
    y = round((local_south - CELL_SIZE / 2) / CELL_SIZE - (0.5 if x & 1 else 0.0))
    if 0 <= x < WIDTH and 0 <= y < HEIGHT:
        return x, y
    return None


def cell_center(x: int, y: int) -> tuple[float, float]:
    return (
        ORIGIN_EASTING + HEX_RADIUS_M + x * HEX_COLUMN_STEP_M,
        ORIGIN_NORTHING + MAP_EXTENT_M
        - (CELL_SIZE / 2 + (y + (0.5 if x & 1 else 0.0)) * CELL_SIZE),
    )


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
    sample_offsets = ((0.0, 0.0), (-0.45, 0.0), (0.45, 0.0),
                      (0.0, -0.38), (0.0, 0.38))
    for feature in features:
        for polygon in polygons(feature.get("geometry") or {}):
            if not polygon or not polygon[0]:
                continue
            eastings = [point[0] for point in polygon[0]]
            northings = [point[1] for point in polygon[0]]
            min_e, max_e = min(eastings), max(eastings)
            min_n, max_n = min(northings), max(northings)
            for y in range(HEIGHT):
                for x in range(WIDTH):
                    center_e, center_n = cell_center(x, y)
                    if not (min_e - HEX_RADIUS_M <= center_e <= max_e + HEX_RADIUS_M and
                            min_n - CELL_SIZE / 2 <= center_n <= max_n + CELL_SIZE / 2):
                        continue
                    hits = 0
                    for offset_x, offset_y in sample_offsets:
                        easting = center_e + offset_x * HEX_RADIUS_M
                        northing = center_n + offset_y * CELL_SIZE
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
                steps = max(1, math.ceil(max(abs(delta_e), abs(delta_n)) / 50.0))
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


def classified_lines(
    directory: Path, source_key: str, classifier: Callable[[dict[str, Any]], int]
) -> list[tuple[list[list[float]], int]]:
    result = []
    for feature in load_features(directory, source_key):
        feature_class = classifier(feature.get("properties") or {})
        for line in line_strings(feature.get("geometry") or {}):
            if len(line) >= 2:
                result.append((line, feature_class))
    return result


def segment_intersection(
    first_start: list[float], first_end: list[float],
    second_start: list[float], second_end: list[float],
) -> tuple[float, float] | None:
    rx = first_end[0] - first_start[0]
    ry = first_end[1] - first_start[1]
    sx = second_end[0] - second_start[0]
    sy = second_end[1] - second_start[1]
    denominator = rx * sy - ry * sx
    if abs(denominator) < 0.000001:
        return None
    qx = second_start[0] - first_start[0]
    qy = second_start[1] - first_start[1]
    road_fraction = (qx * sy - qy * sx) / denominator
    river_fraction = (qx * ry - qy * rx) / denominator
    if not (-0.000001 <= road_fraction <= 1.000001 and
            -0.000001 <= river_fraction <= 1.000001):
        return None
    return (
        first_start[0] + road_fraction * rx,
        first_start[1] + road_fraction * ry,
    )


def road_water_crossings(directory: Path) -> list[dict[str, Any]]:
    """Derive bridges wherever mapped road and water axes truly intersect."""
    roads = classified_lines(directory, "roads", road_class)
    rivers = classified_lines(directory, "water_axis", river_class)
    crossings: list[dict[str, Any]] = []
    for road, road_value in roads:
        for road_start, road_end in zip(road, road[1:]):
            road_min_x, road_max_x = sorted((road_start[0], road_end[0]))
            road_min_y, road_max_y = sorted((road_start[1], road_end[1]))
            for river, river_value in rivers:
                for river_start, river_end in zip(river, river[1:]):
                    if (max(river_start[0], river_end[0]) < road_min_x or
                            min(river_start[0], river_end[0]) > road_max_x or
                            max(river_start[1], river_end[1]) < road_min_y or
                            min(river_start[1], river_end[1]) > road_max_y):
                        continue
                    point = segment_intersection(
                        road_start, road_end, river_start, river_end)
                    if point is None:
                        continue
                    if any(math.hypot(
                        point[0] - existing["point"][0],
                        point[1] - existing["point"][1],
                    ) < 20 for existing in crossings):
                        continue
                    crossings.append({
                        "point": point,
                        "road_start": road_start,
                        "road_end": road_end,
                        "road": road_value,
                        "river": river_value,
                    })
    return crossings


def enrich_cells(
    elevations: list[list[int]], directory: Path,
    crossings: list[dict[str, Any]],
) -> list[list[dict[str, int]]]:
    cells = [[{
        "road": 0, "road_links": 0, "river": 0, "river_links": 0,
        "settlement": 0, "bridge": 0,
        "cultivated": 0, "woods": 0, "marsh": 0, "rough": 0, "water": 0,
    } for _ in range(WIDTH)] for _ in range(HEIGHT)]
    mark_lines(cells, load_features(directory, "roads"), "road", road_class, "road_links")
    mark_lines(cells, load_features(directory, "water_axis"), "river", river_class, "river_links")
    for crossing in crossings:
        cell = cell_for_point(*crossing["point"])
        if cell is not None:
            cells[cell[1]][cell[0]]["bridge"] = 1
    mark_areas(cells, load_features(directory, "cultivated"), "cultivated")
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
                elif features.get("cultivated") or feature_cells is not None:
                    terrain = "cultivated"
                else:
                    terrain = "clear"
                has_features = any(features.values()) or feature_cells is not None
                writer.writerow((
                    MAP_KEY, x, y, elevation, terrain,
                    features.get("road", 0), features.get("road_links", 0),
                    features.get("river", 0), features.get("river_links", 0),
                    min(3, features.get("settlement", 0)),
                    1 if features.get("bridge") else 0,
                    "draft" if has_features else "elevation_only", "",
                ))


def write_edges(
    cells: list[list[dict[str, int]]], output: Path,
    crossings: list[dict[str, Any]],
) -> None:
    rows: dict[tuple[int, int, int, int], dict[str, int | str]] = {}
    for y in range(HEIGHT):
        for x in range(WIDTH):
            cell = cells[y][x]
            for nx, ny, direction, opposite in hex_neighbors(x, y):
                if (ny, nx) <= (y, x):
                    continue
                neighbor = cells[ny][nx]
                road = min(cell["road"], neighbor["road"]) if cell["road_links"] & direction else 0
                river = min(cell["river"], neighbor["river"]) if cell["river_links"] & direction else 0
                if road or river:
                    rows[(x, y, nx, ny)] = {
                        "direction": direction, "road": road, "river": river,
                        "crossing": "none",
                    }

    # Every true geographic road/water intersection is a bridge. Project the
    # road direction onto the closest connected road edge and attach the water
    # class to that traversal edge for W2 movement legality.
    for crossing in crossings:
        cell = cell_for_point(*crossing["point"])
        if cell is None:
            continue
        x, y = cell
        dx = crossing["road_end"][0] - crossing["road_start"][0]
        dy = crossing["road_end"][1] - crossing["road_start"][1]
        candidates = []
        for nx, ny, direction, opposite in hex_neighbors(x, y):
            if not cells[y][x]["road_links"] & direction:
                continue
            center = cell_center(x, y)
            neighbor_center = cell_center(nx, ny)
            edge_dx = neighbor_center[0] - center[0]
            edge_dy = neighbor_center[1] - center[1]
            alignment = abs(dx * edge_dx + dy * edge_dy)
            candidates.append((alignment, nx, ny, direction, opposite))
        if not candidates:
            continue
        _, nx, ny, direction, opposite = max(candidates)
        if (ny, nx) > (y, x):
            key = (x, y, nx, ny)
            stored_direction = direction
        else:
            key = (nx, ny, x, y)
            stored_direction = opposite
        row = rows.setdefault(key, {
            "direction": stored_direction,
            "road": crossing["road"],
            "river": crossing["river"],
            "crossing": "none",
        })
        row["road"] = max(int(row["road"]), int(crossing["road"]))
        row["river"] = max(int(row["river"]), int(crossing["river"]))
        row["crossing"] = "bridge"

    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as target:
        writer = csv.writer(target, lineterminator="\n")
        writer.writerow((
            "map_key", "x", "y", "direction", "neighbor_x", "neighbor_y",
            "road_class", "river_class", "crossing_type", "source_status",
        ))
        for (x, y, nx, ny), row in sorted(rows.items(), key=lambda item: (item[0][1], item[0][0], item[1]["direction"])):
            writer.writerow((
                MAP_KEY, x, y, row["direction"], nx, ny, row["road"],
                row["river"], row["crossing"], "draft",
            ))


def write_feature_paths(
    directory: Path, output: Path, crossings: list[dict[str, Any]]
) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as target:
        writer = csv.writer(target, lineterminator="\n")
        writer.writerow((
            "map_key", "feature_id", "feature_type", "feature_class",
            "sequence", "easting_m", "northing_m", "source_status",
        ))
        feature_id = 0
        definitions = (
            ("road", "roads", road_class),
            ("river", "water_axis", river_class),
        )
        for feature_type, source_key, classifier in definitions:
            for feature in load_features(directory, source_key):
                feature_class = classifier(feature.get("properties") or {})
                for line in line_strings(feature.get("geometry") or {}):
                    if len(line) < 2:
                        continue
                    for sequence, point in enumerate(line):
                        writer.writerow((
                            MAP_KEY, feature_id, feature_type, feature_class,
                            sequence, round(point[0], 3), round(point[1], 3), "draft",
                        ))
                    feature_id += 1

        for crossing in crossings:
            dx = crossing["road_end"][0] - crossing["road_start"][0]
            dy = crossing["road_end"][1] - crossing["road_start"][1]
            length = math.hypot(dx, dy)
            if length < 1:
                continue
            unit_x, unit_y = dx / length, dy / length
            center_x, center_y = crossing["point"]
            half_deck_m = {1: 22, 2: 34, 3: 48}[int(crossing["river"])]
            for sequence, direction in enumerate((-1, 1)):
                writer.writerow((
                    MAP_KEY, feature_id, "bridge", crossing["river"], sequence,
                    round(center_x + direction * unit_x * half_deck_m, 3),
                    round(center_y + direction * unit_y * half_deck_m, 3),
                    "draft",
                ))
            feature_id += 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dgm200", type=Path, help="BKG DGM200 GRID-ASCII ZIP or ASC file")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--edge-output", type=Path, default=DEFAULT_EDGE_OUTPUT)
    parser.add_argument("--feature-output", type=Path, default=DEFAULT_FEATURE_OUTPUT)
    parser.add_argument(
        "--dlm250-dir", type=Path,
        help="Directory containing the clipped BKG DLM250 GeoJSON layer files",
    )
    arguments = parser.parse_args()
    try:
        with open_grid(arguments.dgm200) as source:
            elevations = aggregate_elevations(source)
        crossings = (
            road_water_crossings(arguments.dlm250_dir)
            if arguments.dlm250_dir else []
        )
        feature_cells = (
            enrich_cells(elevations, arguments.dlm250_dir, crossings)
            if arguments.dlm250_dir else None
        )
        write_cells(elevations, arguments.output, feature_cells)
        if feature_cells is not None and arguments.dlm250_dir is not None:
            write_edges(feature_cells, arguments.edge_output, crossings)
            write_feature_paths(arguments.dlm250_dir, arguments.feature_output, crossings)
    except (OSError, zipfile.BadZipFile, KeyError, MapBuildError) as error:
        parser.error(str(error))
    values = [value for row in elevations for value in row]
    print(
        f"Generated {arguments.output}: {WIDTH}x{HEIGHT} hexes at {CELL_SIZE} m, "
        f"elevation {min(values)}..{max(values)} m"
        + (f", DLM250 feature layers applied, {len(crossings)} road/water bridges"
           if arguments.dlm250_dir else "")
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
