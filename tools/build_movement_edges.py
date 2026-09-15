"""Derive hex-edge water barriers from the geographic Point Alpha feature paths."""

from __future__ import annotations

import csv
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
FEATURES_PATH = ROOT / "data" / "maps" / "point_alpha_features.csv"
FEATURE_EDGES_PATH = ROOT / "data" / "maps" / "point_alpha_edges.csv"
OUTPUT_PATH = ROOT / "data" / "maps" / "point_alpha_crossings.csv"
MAP_KEY = "point_alpha_corridor"
WIDTH = 92
HEIGHT = 80
ORIGIN_EASTING = 554000.0
ORIGIN_NORTHING = 5610000.0
EXTENT_WIDTH = 20000.0
EXTENT_HEIGHT = 20000.0


def neighbors(x: int, y: int):
    up = -1 if x % 2 == 0 else 0
    down = 0 if x % 2 == 0 else 1
    for dx, dy in ((0, -1), (1, up), (1, down), (0, 1), (-1, down), (-1, up)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < WIDTH and 0 <= ny < HEIGHT:
            yield nx, ny


def center(x: int, y: int) -> tuple[float, float]:
    world_width = (WIDTH - 1) * 0.75 + 1.0
    world_height = HEIGHT + 0.5
    world_x = x * 0.75 + 0.5
    world_y = y + (0.5 if x % 2 else 0.0) + 0.5
    return (
        ORIGIN_EASTING + world_x / world_width * EXTENT_WIDTH,
        ORIGIN_NORTHING + EXTENT_HEIGHT - world_y / world_height * EXTENT_HEIGHT,
    )


def proper_intersection(a, b, c, d) -> bool:
    denominator = (b[0] - a[0]) * (d[1] - c[1]) - (b[1] - a[1]) * (d[0] - c[0])
    if abs(denominator) < 0.000001:
        return False
    acx, acy = c[0] - a[0], c[1] - a[1]
    t = (acx * (d[1] - c[1]) - acy * (d[0] - c[0])) / denominator
    u = (acx * (b[1] - a[1]) - acy * (b[0] - a[0])) / denominator
    return 0.0001 < t < 0.9999 and -0.0001 <= u <= 1.0001


def load_river_segments():
    features: dict[int, tuple[int, list[tuple[float, float]]]] = {}
    with FEATURES_PATH.open(encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source):
            if row["map_key"] != MAP_KEY or row["feature_type"] != "river":
                continue
            feature_id = int(row["feature_id"])
            feature_class, points = features.setdefault(
                feature_id, (int(row["feature_class"]), []))
            points.append((float(row["easting_m"]), float(row["northing_m"])))
    segments = []
    for feature_class, points in features.values():
        segments.extend((start, end, feature_class) for start, end in zip(points, points[1:]))
    return segments


def load_bridges():
    bridges = {}
    with FEATURE_EDGES_PATH.open(encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source):
            if row["map_key"] != MAP_KEY or row["crossing_type"] != "bridge":
                continue
            first = (int(row["x"]), int(row["y"]))
            second = (int(row["neighbor_x"]), int(row["neighbor_y"]))
            bridges[tuple(sorted((first, second)))] = int(row["river_class"])
    return bridges


def main() -> int:
    rivers = load_river_segments()
    barriers = {}
    for y in range(HEIGHT):
        for x in range(WIDTH):
            first = (x, y)
            first_center = center(x, y)
            for second in neighbors(x, y):
                key = tuple(sorted((first, second)))
                if key in barriers or first > second:
                    continue
                second_center = center(*second)
                river_class = max((feature_class for start, end, feature_class in rivers
                                   if proper_intersection(first_center, second_center, start, end)),
                                  default=0)
                if river_class:
                    barriers[key] = (river_class, "none")

    for key, river_class in load_bridges().items():
        barriers[key] = (river_class, "bridge")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w", encoding="utf-8", newline="") as target:
        writer = csv.writer(target, lineterminator="\n")
        writer.writerow(("map_key", "x", "y", "neighbor_x", "neighbor_y",
                         "river_class", "crossing_type", "source_status"))
        for (first, second), (river_class, crossing_type) in sorted(barriers.items()):
            writer.writerow((MAP_KEY, first[0], first[1], second[0], second[1],
                             river_class, crossing_type, "derived"))
    print(f"Generated {OUTPUT_PATH.relative_to(ROOT)}: {len(barriers)} water crossing edges")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
