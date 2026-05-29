#!/usr/bin/env python3
"""Generate a temporary large cog experiment from the small cog."""

from __future__ import annotations

import copy
import json
import math
from pathlib import Path
from typing import Any

from shapegen import coords, num, write_json


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets/vintagekinematics/shapes/block/cogwheel.json"
OUT = ROOT / "assets/vintagekinematics/shapes/block/largecogwheel-temp2x.json"
CENTER = (8.0, 8.0, 8.0)
RIM_Z = (7.52, 8.48)
HUB_BOTTOM_Z = (6.92, 7.42)
HUB_TOP_Z = (8.58, 9.08)
Z_NUDGE = 0.01
TOOTH_Z_NUDGE = 0.01
RIM_EDGE_INSET = 0.04
BLOCK_SIZE = 16.0


def indexed_nudge(index: int) -> float:
    return index * Z_NUDGE


def load_shape(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def named(shape: dict[str, Any], name: str) -> dict[str, Any]:
    for element in shape["elements"]:
        if element.get("name") == name:
            return element
    raise KeyError(name)


def matching(shape: dict[str, Any], prefix: str) -> list[dict[str, Any]]:
    return [element for element in shape["elements"] if str(element.get("name", "")).startswith(prefix)]


def copy_faces(element: dict[str, Any]) -> dict[str, Any]:
    return copy.deepcopy(element["faces"])


def box_faces(texture: str, from_: list[float], to: list[float]) -> dict[str, Any]:
    x = abs(to[0] - from_[0])
    y = abs(to[1] - from_[1])
    z = abs(to[2] - from_[2])
    return {
        "north": {"texture": texture, "uv": coords([0, 0, min(16, x), min(16, y)])},
        "east": {"texture": texture, "uv": coords([0, 0, min(16, z), min(16, y)])},
        "south": {"texture": texture, "uv": coords([0, 0, min(16, x), min(16, y)])},
        "west": {"texture": texture, "uv": coords([0, 0, min(16, z), min(16, y)])},
        "up": {"texture": texture, "uv": coords([0, 0, min(16, x), min(16, z)])},
        "down": {"texture": texture, "uv": coords([0, 0, min(16, x), min(16, z)])},
    }


def scaled_xy_box(
    source: dict[str, Any],
    *,
    name: str,
    scale: float,
    angle: float,
    z_offset: float = 0,
    z_range: tuple[float, float] | None = None,
    x_inset: float = 0,
    texture: str = "#cog",
) -> dict[str, Any]:
    from_ = [float(value) for value in source["from"]]
    to = [float(value) for value in source["to"]]
    from_[0] += x_inset
    to[0] -= x_inset
    z_from = z_range[0] if z_range is not None else from_[2]
    z_to = z_range[1] if z_range is not None else to[2]

    out_from = [
        CENTER[0] + ((from_[0] - CENTER[0]) * scale),
        CENTER[1] + ((from_[1] - CENTER[1]) * scale),
        z_from + z_offset,
    ]
    out_to = [
        CENTER[0] + ((to[0] - CENTER[0]) * scale),
        CENTER[1] + ((to[1] - CENTER[1]) * scale),
        z_to + z_offset,
    ]

    return {
        "name": name,
        "from": coords(out_from),
        "to": coords(out_to),
        "rotationOrigin": coords(list(CENTER)),
        "faces": box_faces(texture, out_from, out_to),
        "rotationZ": num(angle),
    }


def tooth(source: dict[str, Any], *, index: int, count: int, small_outer_radius: float) -> dict[str, Any]:
    from_ = [float(value) for value in source["from"]]
    to = [float(value) for value in source["to"]]
    width = to[0] - from_[0]
    radial_depth = to[1] - from_[1]
    small_small_overlap = (small_outer_radius * 2) - BLOCK_SIZE
    diagonal_center_distance = math.sqrt((BLOCK_SIZE * BLOCK_SIZE) + (BLOCK_SIZE * BLOCK_SIZE))
    outer_radius = diagonal_center_distance - small_outer_radius + small_small_overlap
    out_from = [
        CENTER[0] - (width / 2),
        CENTER[1] - outer_radius,
        from_[2] + TOOTH_Z_NUDGE,
    ]
    out_to = [
        CENTER[0] + (width / 2),
        CENTER[1] - outer_radius + radial_depth,
        to[2] + TOOTH_Z_NUDGE,
    ]
    angle = index * (360 / count)

    element = {
        "name": f"tooth_{round(angle):03}",
        "from": coords(out_from),
        "to": coords(out_to),
        "rotationOrigin": coords(list(CENTER)),
        "faces": copy_faces(source),
        "rotationZ": num(angle),
    }
    return element


def shaft(source: dict[str, Any]) -> dict[str, Any]:
    element = copy.deepcopy(source)
    element["name"] = "shaft"
    return element


def generate() -> dict[str, Any]:
    source = load_shape(SOURCE)
    source_teeth = matching(source, "tooth_")
    source_tooth = named(source, "tooth_00")
    source_outer = named(source, "hub_outer_00")
    source_top = named(source, "hubcenter_top_00")
    source_bottom = named(source, "hubcenter_bottom_00")
    source_shaft = named(source, "shaft")

    tooth_count = len(source_teeth) * 2
    rim_count = tooth_count // 2
    small_outer_radius = CENTER[1] - float(source_tooth["from"][1])

    elements: list[dict[str, Any]] = []

    for index in range(rim_count):
        angle = index * (180 / rim_count)
        elements.append(
            scaled_xy_box(
                source_outer,
                name=f"rim_{round(angle):03}",
                scale=2,
                angle=angle,
                z_offset=indexed_nudge(index),
                z_range=RIM_Z,
                x_inset=RIM_EDGE_INSET,
            )
        )

    for index in range(tooth_count):
        elements.append(tooth(source_tooth, index=index, count=tooth_count, small_outer_radius=small_outer_radius))

    elements.append(shaft(source_shaft))

    for index in range(rim_count):
        angle = index * (180 / rim_count)
        elements.append(
            scaled_xy_box(
                source_top,
                name=f"hubcenter_top_{round(angle):03}",
                scale=2,
                angle=angle,
                z_offset=indexed_nudge(index),
                z_range=HUB_TOP_Z,
                x_inset=RIM_EDGE_INSET,
            )
        )
        elements.append(
            scaled_xy_box(
                source_bottom,
                name=f"hubcenter_bottom_{round(angle):03}",
                scale=2,
                angle=angle,
                z_offset=-indexed_nudge(index),
                z_range=HUB_BOTTOM_Z,
                x_inset=RIM_EDGE_INSET,
            )
        )

    return {
        "editor": {
            "allAngles": True,
            "entityTextureMode": False,
        },
        "textureWidth": 16,
        "textureHeight": 16,
        "textureSizes": {},
        "textures": {
            "cog": "game:block/wood/planks/generic",
            "wood": "game:block/wood/planks/generic",
        },
        "elements": elements,
    }


def main() -> None:
    shape = generate()
    write_json(OUT, shape)
    print(OUT)


if __name__ == "__main__":
    main()
