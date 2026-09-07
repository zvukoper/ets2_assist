#!/usr/bin/env python3
"""Generate a smooth ETS2 heightmap from static point heights.

Inputs are read from <data-root>/editor_static_data/**/*.json and
<data-root>/localized_cities/cities_sibirmap.json.

The output is a PNG aligned to the computed X/Z bounds plus a JSON sidecar
containing bounds and elevation range. Interpolation uses inverse-distance
weighting over a cKDTree, processed in chunks to keep memory bounded.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
from scipy.spatial import cKDTree


def parse_color(value: str) -> np.ndarray:
    value = value.strip().lstrip("#")
    if len(value) == 3:
        value = "".join(ch * 2 for ch in value)
    if len(value) != 6:
        raise ValueError(f"Invalid color: {value!r}")
    return np.array([int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16)], dtype=np.float32)


def candidates(data: Any) -> list[Any]:
    if isinstance(data, list):
        return data
    if isinstance(data, dict):
        for key in ("objects", "points", "items"):
            value = data.get(key)
            if isinstance(value, list):
                return value
        if data.get("type") == "FeatureCollection" and isinstance(data.get("features"), list):
            return data["features"]
    return []


def number(value: Any) -> float | None:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return None
    return result if math.isfinite(result) else None


def read_samples(data_root: Path) -> list[tuple[float, float, float]]:
    samples: list[tuple[float, float, float]] = []
    static_root = data_root / "editor_static_data"
    if static_root.exists():
        for path in sorted(static_root.rglob("*.json")):
            if path.name.lower() == "meta.json":
                continue
            try:
                data = json.loads(path.read_text(encoding="utf-8-sig"))
            except Exception:
                continue
            for obj in candidates(data):
                if not isinstance(obj, dict):
                    continue
                x = number(obj.get("x"))
                z = number(obj.get("z"))
                y = number(obj.get("y", obj.get("height")))
                pos = obj.get("position")
                if (x is None or z is None) and isinstance(pos, dict):
                    x = number(pos.get("x"))
                    z = number(pos.get("z"))
                    if y is None:
                        y = number(pos.get("y"))
                geometry = obj.get("geometry")
                if (x is None or z is None) and isinstance(geometry, dict) and geometry.get("type") == "Point":
                    coords = geometry.get("coordinates")
                    if isinstance(coords, list) and len(coords) >= 2:
                        x = number(coords[0])
                        z = number(coords[1])
                if x is not None and z is not None and y is not None:
                    samples.append((x, z, y))

    city_path = data_root / "localized_cities" / "cities_sibirmap.json"
    if city_path.exists():
        try:
            data = json.loads(city_path.read_text(encoding="utf-8-sig"))
            for obj in data.get("citiesList", []):
                if not isinstance(obj, dict):
                    continue
                x, z, y = number(obj.get("x")), number(obj.get("z")), number(obj.get("y"))
                if x is not None and z is not None and y is not None:
                    samples.append((x, z, y))
        except Exception:
            pass

    if not samples:
        raise RuntimeError("No points with numeric X/Z/Y were found.")
    return samples


def generate(samples: list[tuple[float, float, float]], out_png: Path, out_meta: Path,
             width: int, low_color: str, high_color: str, neighbors: int, power: float, sigma: float) -> dict[str, Any]:
    pts = np.asarray([(x, z) for x, z, _ in samples], dtype=np.float64)
    heights = np.asarray([y for _, _, y in samples], dtype=np.float64)
    min_x, min_z = pts.min(axis=0)
    max_x, max_z = pts.max(axis=0)
    min_y, max_y = float(heights.min()), float(heights.max())
    span_x, span_z = max_x - min_x, max_z - min_z
    if span_x <= 0 or span_z <= 0:
        raise RuntimeError("Point bounds are degenerate; cannot build a 2D heightmap.")

    height_px = max(1, int(round(width * span_z / span_x)))
    width = max(1, int(width))
    # Image Y grows downward, while map Z grows in the same direction as the editor viewport.
    xs = np.linspace(min_x, max_x, width, dtype=np.float64)
    zs = np.linspace(min_z, max_z, height_px, dtype=np.float64)
    tree = cKDTree(pts)
    k = max(1, min(int(neighbors), len(pts)))
    query_chunk = 65536
    field = np.empty((height_px, width), dtype=np.float32)

    for start in range(0, height_px * width, query_chunk):
        stop = min(start + query_chunk, height_px * width)
        flat = np.arange(start, stop, dtype=np.int64)
        rows = flat // width
        cols = flat % width
        query_points = np.column_stack((xs[cols], zs[rows]))
        distances, indices = tree.query(query_points, k=k, workers=-1)
        if k == 1:
            distances = distances[:, None]
            indices = indices[:, None]
        exact = distances[:, 0] < 1e-9
        values = np.empty(stop - start, dtype=np.float64)
        if np.any(exact):
            values[exact] = heights[indices[exact, 0]]
        non_exact = ~exact
        if np.any(non_exact):
            d = np.maximum(distances[non_exact], 1e-9)
            w = 1.0 / np.power(d, power)
            values[non_exact] = (w * heights[indices[non_exact]]).sum(axis=1) / w.sum(axis=1)
        field.flat[start:stop] = values.astype(np.float32)

    if sigma > 0:
        field = gaussian_filter(field, sigma=float(sigma), mode="nearest").astype(np.float32)

    low = parse_color(low_color)
    high = parse_color(high_color)
    if max_y <= min_y:
        normalized = np.zeros_like(field, dtype=np.float32)
    else:
        normalized = np.clip((field - min_y) / (max_y - min_y), 0.0, 1.0).astype(np.float32)
    rgb = low[None, None, :] + normalized[:, :, None] * (high - low)[None, None, :]
    image = Image.fromarray(np.rint(rgb).astype(np.uint8), mode="RGB")
    out_png.parent.mkdir(parents=True, exist_ok=True)
    out_meta.parent.mkdir(parents=True, exist_ok=True)
    image.save(out_png, format="PNG", optimize=True)

    metadata = {
        "image": out_png.name,
        "minX": min_x,
        "maxX": max_x,
        "minZ": min_z,
        "maxZ": max_z,
        "minY": min_y,
        "maxY": max_y,
        "width": width,
        "height": height_px,
        "samples": len(samples),
        "method": "idw",
        "neighbors": k,
        "power": power,
        "sigma": sigma,
        "lowColor": low_color,
        "highColor": high_color,
    }
    out_meta.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    return metadata


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--width", type=int, default=4096)
    parser.add_argument("--low-color", default="#0f1c06")
    parser.add_argument("--high-color", default="#2d4a18")
    parser.add_argument("--neighbors", type=int, default=12)
    parser.add_argument("--power", type=float, default=2.0)
    parser.add_argument("--sigma", type=float, default=1.15)
    args = parser.parse_args()

    samples = read_samples(args.data_root)
    metadata = generate(samples, args.output, args.metadata, args.width, args.low_color,
                        args.high_color, args.neighbors, args.power, args.sigma)
    print(json.dumps(metadata, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
