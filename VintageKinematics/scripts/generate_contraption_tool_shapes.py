#!/usr/bin/env python3
"""Generate contraption saw and drill block shapes.

The circular cutter/drill surfaces are built from many thin overlapping strips,
like the kinetic sawmill blade. Each repeated strip is offset by 0.01 along the
tool axis so coplanar faces do not z-fight while still reading as a smooth disc.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from shapegen import coords, num, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
SHAPE_DIR = ROOT / "assets/vintagekinematics/shapes/block"
SAW_OUT = SHAPE_DIR / "contraptionsaw.json"
DRILL_OUT = SHAPE_DIR / "contraptiondrill.json"

TEXTURES = {
    "wood": "game:block/wood/planks/generic",
    "iron": "game:block/metal/plate/iron",
    "dark": "game:block/coal/charcoal",
    "steel": "game:block/metal/plate/steel",
}

SIDES = ("north", "east", "south", "west", "up", "down")
TOOL_CENTER = (8.0, 8.0)
COPLANAR_OFFSET = 0.01


def faces(texture: str, uv: list[float] | None = None) -> dict[str, dict[str, Any]]:
    face_uv = coords(uv or [0, 0, 16, 16])
    return {side: {"texture": texture, "uv": face_uv} for side in SIDES}


def box(
    name: str,
    from_: list[float],
    to: list[float],
    texture: str,
    *,
    rotation_origin: list[float] | None = None,
    rotation_x: float | None = None,
    rotation_z: float | None = None,
    uv: list[float] | None = None,
) -> dict[str, Any]:
    element: dict[str, Any] = {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": faces(texture, uv),
    }
    if rotation_origin is not None:
        element["rotationOrigin"] = coords(rotation_origin)
    if rotation_x is not None and abs(rotation_x) > 0.0001:
        element["rotationX"] = num(rotation_x)
    if rotation_z is not None and abs(rotation_z) > 0.0001:
        element["rotationZ"] = num(rotation_z)
    return element


def centered_axis_offset(index: int, count: int, step: float = COPLANAR_OFFSET) -> float:
    return (index - ((count - 1) / 2.0)) * step


def disc_strips(
    *,
    prefix: str,
    center_z: float,
    radius: float,
    strip_width: float,
    thickness: float,
    count: int,
    texture: str,
    angle_start: float = 0.0,
    angle_span: float = 180.0,
    uv: list[float] | None = None,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    cx, cy = TOOL_CENTER
    for index in range(count):
        angle = angle_start + index * angle_span / count
        z = center_z + centered_axis_offset(index, count)
        elements.append(
            box(
                f"{prefix}{index:02d}",
                [cx - strip_width / 2, cy - radius, z - thickness / 2],
                [cx + strip_width / 2, cy + radius, z + thickness / 2],
                texture,
                rotation_origin=[cx, cy, center_z],
                rotation_z=angle,
                uv=uv,
            )
        )
    return elements


def forward_disc_strips(
    *,
    prefix: str,
    center_x: float,
    center_y: float,
    center_z: float,
    radius: float,
    strip_width: float,
    thickness: float,
    count: int,
    texture: str,
    angle_start: float = 0.0,
    angle_span: float = 180.0,
    uv: list[float] | None = None,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    for index in range(count):
        angle = angle_start + index * angle_span / count
        x = center_x + centered_axis_offset(index, count)
        elements.append(
            box(
                f"{prefix}{index:02d}",
                [x - thickness / 2, center_y - radius, center_z - strip_width / 2],
                [x + thickness / 2, center_y + radius, center_z + strip_width / 2],
                texture,
                rotation_origin=[center_x, center_y, center_z],
                rotation_x=angle,
                uv=uv,
            )
        )
    return elements


def radial_teeth(
    *,
    prefix: str,
    center_z: float,
    inner_radius: float,
    outer_radius: float,
    width: float,
    thickness: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    cx, cy = TOOL_CENTER
    for index in range(count):
        angle = index * 360.0 / count
        z = center_z + centered_axis_offset(index, count)
        elements.append(
            box(
                f"{prefix}{index:02d}",
                [cx - width / 2, cy - outer_radius, z - thickness / 2],
                [cx + width / 2, cy - inner_radius, z + thickness / 2],
                texture,
                rotation_origin=[cx, cy, center_z],
                rotation_z=angle,
                uv=[0, 0, 2, 3],
            )
        )
    return elements


def forward_radial_teeth(
    *,
    prefix: str,
    center_x: float,
    center_y: float,
    center_z: float,
    inner_radius: float,
    outer_radius: float,
    width: float,
    thickness: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    for index in range(count):
        angle = index * 360.0 / count
        x = center_x + centered_axis_offset(index, count)
        elements.append(
            box(
                f"{prefix}{index:02d}",
                [x - thickness / 2, center_y - outer_radius, center_z - width / 2],
                [x + thickness / 2, center_y - inner_radius, center_z + width / 2],
                texture,
                rotation_origin=[center_x, center_y, center_z],
                rotation_x=angle,
                uv=[0, 0, 2, 3],
            )
        )
    return elements


def saw_shape() -> dict[str, Any]:
    elements: list[dict[str, Any]] = [
        box("rearMount", [2, 2, 9.5], [14, 14, 15.75], "#wood", uv=[0, 0, 12, 8]),
        box("mountCap", [1.5, 1.5, 8.5], [14.5, 14.5, 10.25], "#iron", uv=[0, 0, 13, 13]),
        box("bearingHousing", [4.5, 4.5, 4.2], [11.5, 11.5, 9.2], "#iron", uv=[0, 0, 7, 7]),
        box("bladeShaft", [3.1, 7.0, 2.4], [12.9, 9.0, 4.4], "#steel", uv=[0, 0, 10, 2]),
        box("upperGuard", [1.0, 8.45, 2.55], [15.0, 14.75, 4.0], "#iron", uv=[0, 0, 14, 6]),
        box("leftGuardCheek", [0.8, 2.0, 2.45], [2.15, 13.8, 4.05], "#iron", uv=[0, 0, 12, 2]),
        box("rightGuardCheek", [13.85, 2.0, 2.45], [15.2, 13.8, 4.05], "#iron", uv=[0, 0, 12, 2]),
    ]

    elements.extend(
        forward_disc_strips(
            prefix="bladeStrip",
            center_x=8.0,
            center_y=8.0,
            center_z=2.2,
            radius=6.05,
            strip_width=1.15,
            thickness=0.07,
            count=24,
            texture="#steel",
            uv=[3, 7, 13, 9],
        )
    )
    elements.extend(
        forward_radial_teeth(
            prefix="bladeTooth",
            center_x=8.0,
            center_y=8.0,
            center_z=2.2,
            inner_radius=5.9,
            outer_radius=6.75,
            width=0.9,
            thickness=0.08,
            count=24,
            texture="#steel",
        )
    )
    elements.extend(
        forward_disc_strips(
            prefix="hubRound",
            center_x=8.0,
            center_y=8.0,
            center_z=2.2,
            radius=1.45,
            strip_width=0.65,
            thickness=0.36,
            count=10,
            texture="#iron",
            uv=[6, 6, 10, 10],
        )
    )

    return shape_document(textures=TEXTURES, elements=elements, texture_width=16, texture_height=16)


def drill_cylinder(
    *,
    prefix: str,
    z_from: float,
    z_to: float,
    radius: float,
    strip_width: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    cx, cy = TOOL_CENTER
    for index in range(count):
        angle = index * 180.0 / count
        z_offset = centered_axis_offset(index, count)
        elements.append(
            box(
                f"{prefix}{index:02d}",
                [cx - strip_width / 2, cy - radius, z_from + z_offset],
                [cx + strip_width / 2, cy + radius, z_to + z_offset],
                texture,
                rotation_origin=[cx, cy, 0],
                rotation_z=angle,
                uv=[0, 0, 10, 2],
            )
        )
    return elements


def drill_flutes() -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    cx, cy = TOOL_CENTER
    segments = 14
    for side in range(2):
        for index in range(segments):
            z1 = -5.15 + index * 0.78
            z2 = z1 + 1.1
            angle = side * 180.0 + index * 28.0
            z_offset = centered_axis_offset(index, segments)
            elements.append(
                box(
                    f"drillFlute{side}_{index:02d}",
                    [cx - 0.34, cy + 0.85, z1 + z_offset],
                    [cx + 0.34, cy + 2.7, z2 + z_offset],
                    "#steel",
                    rotation_origin=[cx, cy, 0],
                    rotation_z=angle,
                    uv=[0, 0, 2, 3],
                )
            )
    return elements


def drill_shape() -> dict[str, Any]:
    elements: list[dict[str, Any]] = [
        box("rearMount", [2, 2, 9.5], [14, 14, 15.75], "#wood", uv=[0, 0, 12, 8]),
        box("mountCap", [1.5, 1.5, 8.5], [14.5, 14.5, 10.25], "#iron", uv=[0, 0, 13, 13]),
        box("bearingHousing", [4.25, 4.25, 4.7], [11.75, 11.75, 9.4], "#iron", uv=[0, 0, 8, 8]),
        box("darkCollar", [5.6, 5.6, 3.7], [10.4, 10.4, 5.0], "#dark", uv=[0, 0, 5, 5]),
    ]

    elements.extend(
        drill_cylinder(
            prefix="drillCore",
            z_from=-5.05,
            z_to=5.3,
            radius=1.25,
            strip_width=0.65,
            count=12,
            texture="#steel",
        )
    )
    elements.extend(drill_flutes())
    elements.extend(
        drill_cylinder(
            prefix="drillTip",
            z_from=-6.15,
            z_to=-4.95,
            radius=0.72,
            strip_width=0.42,
            count=8,
            texture="#steel",
        )
    )

    return shape_document(textures=TEXTURES, elements=elements, texture_width=16, texture_height=16)


def main() -> None:
    write_json(SAW_OUT, saw_shape())
    write_json(DRILL_OUT, drill_shape())
    print(SAW_OUT)
    print(DRILL_OUT)


if __name__ == "__main__":
    main()
