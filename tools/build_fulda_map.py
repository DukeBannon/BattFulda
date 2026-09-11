"""Aggregate official BKG DGM200 elevations into the 500 m Point Alpha grid."""

from __future__ import annotations

import argparse
import csv
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator, TextIO
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


def write_cells(elevations: list[list[int]], output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as target:
        writer = csv.writer(target, lineterminator="\n")
        writer.writerow((
            "map_key", "x", "y", "elevation_m", "terrain_key", "road_class",
            "river_class", "settlement_level", "has_bridge", "source_status", "notes",
        ))
        for y, row in enumerate(elevations):
            for x, elevation in enumerate(row):
                writer.writerow((
                    MAP_KEY, x, y, elevation, "clear", 0, 0, 0, 0,
                    "elevation_only", "",
                ))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dgm200", type=Path, help="BKG DGM200 GRID-ASCII ZIP or ASC file")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    arguments = parser.parse_args()
    try:
        with open_grid(arguments.dgm200) as source:
            elevations = aggregate_elevations(source)
        write_cells(elevations, arguments.output)
    except (OSError, zipfile.BadZipFile, KeyError, MapBuildError) as error:
        parser.error(str(error))
    values = [value for row in elevations for value in row]
    print(
        f"Generated {arguments.output}: {WIDTH}x{HEIGHT} cells, "
        f"elevation {min(values)}..{max(values)} m"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
