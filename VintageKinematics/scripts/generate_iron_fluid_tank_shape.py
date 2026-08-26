#!/usr/bin/env python3
"""Generate the compact iron fluid tank model."""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any

from shapegen import coords, num, shape_document, simple_faces, write_json


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/vintagekinematics/shapes/block/ironfluidtank.json"
TEXTURES = {
    "iron": "game:block/metal/plate/iron",
    "band": "game:block/metal/ingot/iron",
    "dark": "game:block/metal/plate/steel",
}
SEGMENT_Y_STEP = 0.01


def box(name: str, from_: list[float], to: list[float], texture: str) -> dict[str, Any]:
    return {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": simple_faces(texture),
    }


def shell(prefix: str, y1: float, y2: float, radius: float, depth: float, count: int, texture: str) -> list[dict[str, Any]]:
    circumference = 2 * math.pi * radius
    width = circumference / count * 1.035
    elements: list[dict[str, Any]] = []

    for index in range(count):
        angle = index * 360 / count
        y_offset = (index - (count - 1) / 2) * SEGMENT_Y_STEP
        element = box(
            f"{prefix}{index:02d}",
            [8 - width / 2, y1 + y_offset, 8 + radius - depth / 2],
            [8 + width / 2, y2 + y_offset, 8 + radius + depth / 2],
            texture,
        )
        element["rotationOrigin"] = [8, 8, 8]
        element["rotationY"] = num(angle)
        elements.append(element)

    return elements


def radial_cap(
    prefix: str,
    y1: float,
    radius: float,
    depth: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    circumference = 2 * math.pi * radius
    width = circumference / count * 1.035
    elements: list[dict[str, Any]] = []

    for index in range(count):
        angle = index * 360 / count
        y_offset = (index - (count - 1) / 2) * SEGMENT_Y_STEP
        element = box(
            f"{prefix}{index:02d}",
            [8 - width / 2, y1 + y_offset, 8],
            [8 + width / 2, y1 + 1 + y_offset, 8 + radius + depth / 2],
            texture,
        )
        element["rotationOrigin"] = [8, 8, 8]
        element["rotationY"] = num(angle)
        elements.append(element)

    return elements


def build_shape() -> dict[str, Any]:
    elements: list[dict[str, Any]] = []
    elements.extend(shell("tankWall", 1.25, 14.75, 5.8, 0.75, 12, "#iron"))
    elements.extend(shell("lowerBand", 2.0, 3.0, 6.18, 0.78, 12, "#band"))
    elements.extend(shell("upperBand", 12.9, 13.9, 6.18, 0.78, 12, "#band"))

    # Radial segments close both ends to the center. Their small Y stagger prevents
    # coplanar faces where neighboring rotated segments overlap.
    elements.extend(radial_cap("bottomCap", 0.3, 5.8, 0.75, 12, "#iron"))
    elements.extend(radial_cap("topCap", 14.7, 5.8, 0.75, 12, "#iron"))
    elements.extend(
        [
            box("fillNeck", [6.65, 15.72, 6.65], [9.35, 15.87, 9.35], "#band"),
            box("fillCap", [6.25, 15.84, 6.25], [9.75, 16, 9.75], "#dark"),
            box("footNorthWest", [2.8, 0, 3.2], [4.7, 1.1, 5.1], "#dark"),
            box("footNorthEast", [11.3, 0, 3.2], [13.2, 1.1, 5.1], "#dark"),
            box("footSouthWest", [2.8, 0, 10.9], [4.7, 1.1, 12.8], "#dark"),
            box("footSouthEast", [11.3, 0, 10.9], [13.2, 1.1, 12.8], "#dark"),
            box("frontPort", [6.6, 6.55, 1.55], [9.4, 9.45, 2.45], "#dark"),
            box("frontPortBoss", [7.1, 7.05, 0.75], [8.9, 8.95, 1.75], "#band"),
        ]
    )

    return shape_document(textures=TEXTURES, elements=elements, texture_width=32, texture_height=32)


if __name__ == "__main__":
    write_json(OUTPUT, build_shape())
