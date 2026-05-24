#!/usr/bin/env python3
"""Regenerate the powered saw circular blade strips."""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SHAPE_PATH = ROOT / "assets/vintagekinematics/shapes/item/tool/poweredsaw.json"

CENTER_X = 15.72
CENTER_Y = 8.0
CENTER_Z = 8.5
RADIUS = 5.15
STRIP_WIDTH = 1.15
THICKNESS = 0.06
COUNT = 16
X_OFFSET_STEP = 0.01


def metal_faces(long_uv: list[float], cap_uv: list[float]) -> dict[str, dict[str, object]]:
    return {
        "north": {"texture": "#metal", "uv": cap_uv},
        "east": {"texture": "#metal", "uv": long_uv},
        "south": {"texture": "#metal", "uv": cap_uv},
        "west": {"texture": "#metal", "uv": long_uv},
        "up": {"texture": "#metal", "uv": cap_uv},
        "down": {"texture": "#metal", "uv": cap_uv},
    }


def blade_strip(index: int) -> dict[str, object]:
    angle = round(index * 180.0 / COUNT, 3)
    x_offset = (index - ((COUNT - 1) / 2.0)) * X_OFFSET_STEP
    x1 = round(CENTER_X - THICKNESS / 2.0 + x_offset, 3)
    x2 = round(CENTER_X + THICKNESS / 2.0 + x_offset, 3)
    y1 = round(CENTER_Y - RADIUS, 3)
    y2 = round(CENTER_Y + RADIUS, 3)
    z1 = round(CENTER_Z - STRIP_WIDTH / 2.0, 3)
    z2 = round(CENTER_Z + STRIP_WIDTH / 2.0, 3)

    element: dict[str, object] = {
        "name": f"bladeStrip{index:02d}",
        "from": [x1, y1, z1],
        "to": [x2, y2, z2],
        "rotationOrigin": [CENTER_X, CENTER_Y, CENTER_Z],
        "faces": metal_faces([3.0, 7.0, 13.0, 9.0], [0.0, 7.0, 1.0, 9.0]),
    }
    if angle:
        element["rotationX"] = angle
    return element


def main() -> None:
    shape = json.loads(SHAPE_PATH.read_text())
    blade = next(element for element in shape["elements"] if element["name"] == "blade")
    children = blade["children"]
    blade["children"] = [
        child
        for child in children
        if not child["name"].startswith("bladeVertical")
        and not child["name"].startswith("bladeHorizontal")
        and not child["name"].startswith("bladeDiagonal")
        and not child["name"].startswith("bladeStrip")
    ]

    strips = [blade_strip(i) for i in range(COUNT)]
    hub_index = next(
        (i for i, child in enumerate(blade["children"]) if child["name"] == "bladeHub"),
        len(blade["children"]),
    )
    blade["children"][hub_index:hub_index] = strips

    SHAPE_PATH.write_text(json.dumps(shape, indent="\t") + "\n")


if __name__ == "__main__":
    main()
