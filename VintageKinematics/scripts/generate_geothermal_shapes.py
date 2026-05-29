#!/usr/bin/env python3
"""Generate geothermal bore and steam engine shapes."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from shapegen import coords, num, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
SHAPE_DIR = ROOT / "assets/vintagekinematics/shapes/block"
ENGINE_OUT = SHAPE_DIR / "geothermalsteamengine.json"
BORE_OUT = SHAPE_DIR / "geothermalbore.json"
COPLANAR_OFFSET = 0.01
SIDES = ("north", "east", "south", "west", "up", "down")

TEXTURES = {
    "steel": "game:block/metal/plate/steel",
    "iron": "game:block/metal/plate/iron",
    "brass": "game:block/metal/ingot/brass",
    "copper": "game:block/metal/plate/copper",
    "dark": "game:block/coal/charcoal",
    "heat": "game:block/coal/charcoal",
    "glow": "vintagekinematics:block/kineticigniter-rod-glow",
    "water": "game:block/liquid/water",
}


def uv(values: list[float] | None = None) -> list[float]:
    return coords(values or [0, 0, 16, 16])


def faces(texture: str, face_uv: list[float] | None = None, **overrides: str) -> dict[str, dict[str, Any]]:
    result = {side: {"texture": texture, "uv": uv(face_uv)} for side in SIDES}
    for side, tex in overrides.items():
        if tex is not None:
            result[side]["texture"] = tex
    return result


def box(
    name: str,
    from_: list[float],
    to: list[float],
    texture: str,
    *,
    rotation_origin: list[float] | None = None,
    rotation_x: float | None = None,
    rotation_y: float | None = None,
    rotation_z: float | None = None,
    face_uv: list[float] | None = None,
    children: list[dict[str, Any]] | None = None,
    **face_overrides: str,
) -> dict[str, Any]:
    element: dict[str, Any] = {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": faces(texture, face_uv, **face_overrides),
    }
    if rotation_origin is not None:
        element["rotationOrigin"] = coords(rotation_origin)
    if rotation_x is not None and abs(rotation_x) > 0.0001:
        element["rotationX"] = num(rotation_x)
    if rotation_y is not None and abs(rotation_y) > 0.0001:
        element["rotationY"] = num(rotation_y)
    if rotation_z is not None and abs(rotation_z) > 0.0001:
        element["rotationZ"] = num(rotation_z)
    if children:
        element["children"] = children
    return element


def offset(index: int, count: int, step: float = COPLANAR_OFFSET) -> float:
    return (index - (count - 1) / 2.0) * step


def yz_ring_segments(
    prefix: str,
    *,
    center_x: float,
    center_y: float,
    center_z: float,
    radius: float,
    radial_width: float,
    depth: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    tangential_len = 2.0 * 3.14159 * radius / count * 0.94
    for i in range(count):
        angle = i * 360.0 / count
        x = center_x + offset(i, count)
        elements.append(
            box(
                f"{prefix}{i:02d}",
                [x - depth / 2, center_y + radius - radial_width / 2, center_z - tangential_len / 2],
                [x + depth / 2, center_y + radius + radial_width / 2, center_z + tangential_len / 2],
                texture,
                rotation_origin=[center_x, center_y, center_z],
                rotation_x=angle,
                face_uv=[0, 0, 8, 8],
            )
        )
    return elements


def xy_ring_segments(
    prefix: str,
    *,
    center_x: float,
    center_y: float,
    center_z: float,
    radius: float,
    radial_width: float,
    depth: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    tangential_len = 2.0 * 3.14159 * radius / count * 0.94
    for i in range(count):
        angle = i * 360.0 / count
        z = center_z + offset(i, count)
        elements.append(
            box(
                f"{prefix}{i:02d}",
                [center_x - tangential_len / 2, center_y + radius - radial_width / 2, z - depth / 2],
                [center_x + tangential_len / 2, center_y + radius + radial_width / 2, z + depth / 2],
                texture,
                rotation_origin=[center_x, center_y, center_z],
                rotation_z=angle,
                face_uv=[0, 0, 8, 8],
            )
        )
    return elements


def round_tube_segments(
    prefix: str,
    *,
    axis: str,
    start: float,
    end: float,
    center_x: float,
    center_y: float,
    center_z: float,
    radius: float,
    wall_depth: float,
    strip_width: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    for i in range(count):
        angle = i * 360.0 / count
        thin = offset(i, count, COPLANAR_OFFSET * 0.5)
        inner = radius - wall_depth / 2.0 - thin
        outer = radius + wall_depth / 2.0 + thin
        half_width = strip_width / 2.0 + abs(thin)

        if axis == "x":
            elements.append(
                box(
                    f"{prefix}{i:02d}",
                    [start, center_y - half_width, center_z + inner],
                    [end, center_y + half_width, center_z + outer],
                    texture,
                    rotation_origin=[center_x, center_y, center_z],
                    rotation_x=angle,
                    face_uv=[0, 0, abs(end - start), wall_depth],
                )
            )
        elif axis == "z":
            elements.append(
                box(
                    f"{prefix}{i:02d}",
                    [center_x - half_width, center_y + inner, start],
                    [center_x + half_width, center_y + outer, end],
                    texture,
                    rotation_origin=[center_x, center_y, center_z],
                    rotation_z=angle,
                    face_uv=[0, 0, abs(end - start), wall_depth],
                )
            )
        else:
            elements.append(
                box(
                    f"{prefix}{i:02d}",
                    [center_x - half_width, start, center_z + inner],
                    [center_x + half_width, end, center_z + outer],
                    texture,
                    rotation_origin=[center_x, center_y, center_z],
                    rotation_y=angle,
                    face_uv=[0, 0, abs(end - start), wall_depth],
                )
            )
    return elements


def z_cylinder_strips(
    prefix: str,
    *,
    z1: float,
    z2: float,
    center_x: float,
    center_y: float,
    radius: float,
    strip_width: float,
    count: int,
    texture: str,
) -> list[dict[str, Any]]:
    elements: list[dict[str, Any]] = []
    for i in range(count):
        angle = i * 180 / count
        side_offset = offset(i, count)
        elements.append(
            box(
                f"{prefix}{i:02d}",
                [center_x - strip_width / 2 + side_offset, center_y - radius, z1],
                [center_x + strip_width / 2 + side_offset, center_y + radius, z2],
                texture,
                rotation_origin=[center_x, center_y, (z1 + z2) / 2],
                rotation_z=angle,
                face_uv=[0, 0, abs(z2 - z1), radius * 2],
            )
        )
    return elements


def parent(name: str, origin: list[float], children: list[dict[str, Any]]) -> dict[str, Any]:
    local_children = [relative_to(child, origin) for child in children]
    return {
        "name": name,
        "from": coords(origin),
        "to": coords(origin),
        "rotationOrigin": coords(origin),
        "faces": {},
        "children": local_children,
    }


def relative_to(element: dict[str, Any], origin: list[float]) -> dict[str, Any]:
    result = dict(element)
    if "from" in result:
        result["from"] = coords([result["from"][0] - origin[0], result["from"][1] - origin[1], result["from"][2] - origin[2]])
    if "to" in result:
        result["to"] = coords([result["to"][0] - origin[0], result["to"][1] - origin[1], result["to"][2] - origin[2]])
    if "rotationOrigin" in result:
        result["rotationOrigin"] = coords(
            [
                result["rotationOrigin"][0] - origin[0],
                result["rotationOrigin"][1] - origin[1],
                result["rotationOrigin"][2] - origin[2],
            ]
        )
    return result


def steam_engine_shape() -> dict[str, Any]:
    drive_shaft_children = round_tube_segments(
        "driveShaftWall",
        axis="x",
        start=-0.01,
        end=16.01,
        center_x=8,
        center_y=8,
        center_z=8,
        radius=0.9,
        wall_depth=0.42,
        strip_width=0.5,
        count=12,
        texture="#steel",
    )
    drive_shaft_children.extend(
        [
            box("driveShaftKeyTop", [-0.01, 9.13, 7.74], [16.01, 9.43, 8.26], "#brass"),
            box("driveShaftKeySouth", [-0.01, 7.74, 9.13], [16.01, 8.26, 9.43], "#brass"),
        ]
    )

    turbine_children = []
    turbine_children.extend(yz_ring_segments("turbineRotorRing", center_x=1.8, center_y=8, center_z=8, radius=2.25, radial_width=0.46, depth=0.34, count=12, texture="#steel"))
    turbine_children.extend(
        [
            box("turbineVaneA", [1.6, 7.65, 5.65], [2.0, 8.35, 10.35], "#brass"),
            box("turbineVaneB", [1.58, 7.65, 5.65], [2.02, 8.35, 10.35], "#brass", rotation_origin=[1.8, 8, 8], rotation_x=60),
            box("turbineVaneC", [1.56, 7.65, 5.65], [2.04, 8.35, 10.35], "#brass", rotation_origin=[1.8, 8, 8], rotation_x=120),
            box("turbineHub", [1.45, 7.05, 7.05], [2.15, 8.95, 8.95], "#brass"),
        ]
    )
    heat_fins = [
        box(f"heatReceiverFin{i:02d}", [4.35 + i * 1.15, 7.55, 2.04], [4.83 + i * 1.15, 13.25, 2.72], "#copper")
        for i in range(7)
    ]
    water_inlet = round_tube_segments(
        "waterInletPipe",
        axis="z",
        start=13.9,
        end=16.55,
        center_x=8.0,
        center_y=8.0,
        center_z=15.2,
        radius=1.45,
        wall_depth=0.42,
        strip_width=0.72,
        count=16,
        texture="#copper",
    )
    inlet_flange = xy_ring_segments(
        "waterInletFlange",
        center_x=8.0,
        center_y=8.0,
        center_z=15.85,
        radius=2.1,
        radial_width=0.45,
        depth=0.5,
        count=14,
        texture="#brass",
    )
    water_feed_pipe = round_tube_segments(
        "waterFeedPipe",
        axis="z",
        start=8.9,
        end=14.05,
        center_x=8.0,
        center_y=8.0,
        center_z=11.6,
        radius=0.72,
        wall_depth=0.34,
        strip_width=0.42,
        count=12,
        texture="#copper",
    )

    elements = [
        box("foundationSlab", [-0.7, 0, 0.75], [16.7, 1.35, 15.25], "#steel", down="#dark"),
        box("basePlinth", [0.9, 1.45, 1.55], [15.1, 4.0, 14.45], "#iron"),
        box("lowerTurbineBedBase", [1.2, 4.05, 3.1], [15.0, 6.45, 12.9], "#dark"),
        box("lowerTurbineBedNorthShoulder", [1.2, 6.45, 3.1], [15.0, 9.2, 6.49], "#dark"),
        box("lowerTurbineBedSouthShoulder", [1.2, 6.45, 9.51], [15.0, 9.2, 12.9], "#dark"),
        box("heatReceiverBody", [3.7, 7.55, -0.35], [12.3, 13.25, 2.1], "#dark"),
        box("heatReceiverFrameTop", [3.2, 13.25, -0.45], [12.8, 14.0, 2.35], "#copper"),
        box("heatReceiverFrameBottom", [3.2, 6.8, -0.45], [12.8, 7.55, 2.35], "#copper"),
        box("heatReceiverFrameLeft", [3.05, 7.55, -0.45], [3.8, 13.25, 2.35], "#copper"),
        box("heatReceiverFrameRight", [12.2, 7.55, -0.45], [12.95, 13.25, 2.35], "#copper"),
        box("heatGlowCore", [4.3, 7.8, -0.55], [11.7, 12.8, -0.25], "#heat"),
        box("heatReceiverThroat", [4.2, 8.05, 2.05], [11.8, 12.55, 3.15], "#dark"),
        box("heatReceiverThroatBand", [3.95, 7.8, 2.95], [12.05, 12.8, 3.55], "#copper"),
        box("heatTransferDuct", [5.25, 8.35, 3.05], [10.75, 12.25, 10.25], "#dark"),
        box("heatTransferDuctGlowTop", [5.5, 12.2, 3.4], [10.5, 12.65, 9.9], "#heat"),
        *heat_fins,
        box("boilerLowerShell", [3.0, 10.0, 3.0], [14.2, 20.4, 13.2], "#steel"),
        box("boilerUpperShell", [4.2, 20.4, 4.0], [13.0, 28.6, 12.2], "#steel"),
        box("boilerTopCap", [5.3, 28.6, 5.1], [11.9, 30.6, 11.1], "#brass"),
        box("waterSightGlass", [14.24, 15.2, 5.3], [14.54, 25.8, 10.7], "#water"),
        box("sightGlassFrameTop", [14.12, 25.75, 5.05], [14.72, 26.28, 10.95], "#brass"),
        box("sightGlassFrameBottom", [14.12, 14.72, 5.05], [14.72, 15.25, 10.95], "#brass"),
        box("sightGlassFrameLeft", [14.1, 15.25, 5.0], [14.74, 25.75, 5.35], "#brass"),
        box("sightGlassFrameRight", [14.1, 15.25, 10.65], [14.74, 25.75, 11.0], "#brass"),
        box("waterFeedSocket", [6.75, 6.85, 12.75], [9.25, 9.15, 14.2], "#brass"),
        *water_feed_pipe,
        box("waterFeedElbow", [6.9, 6.9, 8.45], [9.1, 9.1, 9.25], "#copper"),
        box("waterFeedRiser", [6.95, 8.9, 8.45], [9.05, 12.35, 9.25], "#copper"),
        box("waterFeedBoilerBoss", [6.6, 12.1, 8.15], [9.4, 13.0, 9.55], "#brass"),
        *water_inlet,
        *inlet_flange,
        box("waterInletValveStem", [7.55, 9.25, 14.85], [8.45, 10.8, 15.45], "#brass"),
        box("waterInletValveHandle", [7.25, 10.62, 13.95], [8.75, 11.12, 16.35], "#brass"),
        box("steamDome", [6.5, 29.65, 5.4], [11.5, 31.6, 10.6], "#brass"),
        box("pressureValve", [8.05, 31.32, 7.0], [9.75, 32.0, 9.0], "#brass"),
        box("turbineHousingBackplateNorth", [3.19, 4.7, 4.7], [4.36, 11.35, 6.49], "#iron"),
        box("turbineHousingBackplateSouth", [3.19, 4.7, 9.51], [4.36, 11.35, 11.3], "#iron"),
        box("turbineHousingBackplateTop", [3.19, 9.51, 6.49], [4.36, 11.35, 9.51], "#iron"),
        box("turbineHousingBackplateBottom", [3.19, 4.7, 6.49], [4.36, 6.49, 9.51], "#iron"),
        box("turbineHousingBottomRail", [0.64, 4.17, 4.34], [4.36, 4.81, 11.66], "#steel"),
        box("turbineHousingTopRail", [0.64, 11.19, 4.34], [4.36, 11.83, 11.66], "#steel"),
        box("turbineHousingWestRail", [0.64, 4.81, 4.34], [4.36, 11.19, 4.94], "#steel"),
        box("turbineHousingEastRail", [0.64, 4.81, 11.06], [4.36, 11.19, 11.66], "#steel"),
        box("turbineBearingNorth", [0.34, 6.42, 6.42], [1.26, 9.58, 6.94], "#iron"),
        box("turbineBearingSouth", [0.34, 6.42, 9.06], [1.26, 9.58, 9.58], "#iron"),
        box("turbineBearingTop", [0.34, 9.06, 6.94], [1.26, 9.58, 9.06], "#iron"),
        box("turbineBearingBottom", [0.34, 6.42, 6.94], [1.26, 6.94, 9.06], "#iron"),
        parent("driveShaft", [8, 8, 8], drive_shaft_children),
        parent("turbineRotor", [1.8, 8, 8], turbine_children),
        box("stack", [11.9, 22.8, 6.0], [14.5, 32.0, 10.0], "#dark"),
        box("stackBandLowerNorth", [11.72, 23.2, 5.72], [14.68, 24.0, 5.92], "#brass"),
        box("stackBandLowerSouth", [11.72, 23.2, 10.08], [14.68, 24.0, 10.28], "#brass"),
        box("stackBandUpperNorth", [11.72, 29.7, 5.72], [14.68, 30.5, 5.92], "#brass"),
        box("stackBandUpperSouth", [11.72, 29.7, 10.08], [14.68, 30.5, 10.28], "#brass"),
    ]
    return shape_document(textures=TEXTURES, elements=elements, texture_width=32, texture_height=32)


def geothermal_bore_shape() -> dict[str, Any]:
    shaft_children = []
    shaft_children.extend(
        z_cylinder_strips(
            "shaftCore",
            z1=42,
            z2=50,
            center_x=24,
            center_y=24,
            radius=2.0,
            strip_width=0.55,
            count=10,
            texture="#steel",
        )
    )
    shaft_children.extend(xy_ring_segments("driveCollar", center_x=24, center_y=24, center_z=42.5, radius=4.1, radial_width=0.65, depth=0.24, count=16, texture="#brass"))
    heat_port_fins = [
        box(f"heatOutputFin{i:02d}", [20.05 + i * 1.85, 7.55, -0.02], [20.55 + i * 1.85, 13.25, 1.25], "#copper")
        for i in range(5)
    ]
    core_coil = round_tube_segments(
        "heatCoreCoil",
        axis="y",
        start=5.4,
        end=18.2,
        center_x=24,
        center_y=12,
        center_z=24,
        radius=7.0,
        wall_depth=0.55,
        strip_width=1.1,
        count=18,
        texture="#copper",
    )
    tap_duct = round_tube_segments(
        "heatOutputDuct",
        axis="z",
        start=0.1,
        end=14.8,
        center_x=24,
        center_y=10.9,
        center_z=7.4,
        radius=2.65,
        wall_depth=0.65,
        strip_width=0.85,
        count=16,
        texture="#copper",
    )

    elements = [
        box("foundationFrameNorth", [2, 0, 2], [46, 3, 6], "#steel"),
        box("foundationFrameSouth", [2, 0, 42], [46, 3, 46], "#steel"),
        box("foundationFrameWest", [2, 0, 6], [6, 3, 42], "#steel"),
        box("foundationFrameEast", [42, 0, 6], [46, 3, 42], "#steel"),
        box("centerServiceDeck", [8, 3, 8], [40, 5.2, 40], "#iron", up="#dark"),
        box("northPipeRack", [7, 5.2, 3.2], [41, 8.2, 6.2], "#brass"),
        box("southPipeRack", [7, 5.2, 41.8], [41, 8.2, 44.8], "#brass"),
        box("westBearingBlock", [6.5, 8.0, 18], [12.5, 19.5, 30], "#iron"),
        box("eastBearingBlock", [35.5, 8.0, 18], [41.5, 19.5, 30], "#iron"),
        box("northBearingBlock", [18, 8.0, 6.5], [30, 19.5, 12.5], "#iron"),
        box("southBearingBlock", [18, 8.0, 35.5], [30, 19.5, 41.5], "#iron"),
        box("southDriveTowerLeft", [17.99, 5.2, 37.19], [20.99, 20.2, 42.11], "#iron"),
        box("southDriveTowerRight", [27.01, 5.2, 37.19], [30.01, 20.2, 42.11], "#iron"),
        box("southDriveBearingBase", [18.0, 18.8, 37.2], [30.0, 21.4, 42.4], "#iron"),
        box("southDriveBearingCap", [18.0, 26.1, 37.2], [30.0, 27.8, 42.4], "#iron"),
        box("southDriveSocket", [19.2, 20.8, 40.9], [28.8, 27.1, 43.6], "#steel"),
        box("southDriveSocketBand", [20.0, 21.6, 43.45], [28.0, 26.3, 44.25], "#brass"),
        box("pressureManifold", [14, 18.5, 14], [34, 25.5, 34], "#brass"),
        box("steamTapValve", [20, 25.2, 20], [28, 31.5, 28], "#brass"),
        box("leftValveWheel", [12, 21, 22.4], [16, 25, 25.6], "#steel"),
        box("rightValveWheel", [32, 21, 22.4], [36, 25, 25.6], "#steel"),
        box("heatShieldNorth", [12, 5.4, 10.0], [36, 10.6, 12.1], "#copper"),
        box("heatShieldSouth", [12, 5.4, 35.9], [36, 10.6, 38.0], "#copper"),
        box("heatShieldWest", [10.0, 5.4, 12], [12.1, 10.6, 36], "#copper"),
        box("heatShieldEast", [35.9, 5.4, 12], [38.0, 10.6, 36], "#copper"),
        box("heatChamberBase", [13.2, 5.2, 13.2], [34.8, 7.4, 34.8], "#dark"),
        box("heatChamberCap", [13.2, 16.3, 13.2], [34.8, 18.6, 34.8], "#dark"),
        box("heatChamberNorthWall", [13.2, 7.4, 13.0], [34.8, 16.3, 15.2], "#dark"),
        box("heatChamberSouthWall", [13.2, 7.4, 32.8], [34.8, 16.3, 35.0], "#dark"),
        box("heatChamberWestWall", [13.0, 7.4, 15.2], [15.2, 16.3, 32.8], "#dark"),
        box("heatChamberEastWall", [32.8, 7.4, 15.2], [35.0, 16.3, 32.8], "#dark"),
        box("heatCoreGlowNorth", [16.2, 8.0, 15.05], [31.8, 15.4, 15.55], "#heat"),
        box("heatCoreGlowSouth", [16.2, 8.0, 32.45], [31.8, 15.4, 32.95], "#heat"),
        box("heatCoreGlowWest", [15.05, 8.0, 16.2], [15.55, 15.4, 31.8], "#heat"),
        box("heatCoreGlowEast", [32.45, 8.0, 16.2], [32.95, 15.4, 31.8], "#heat"),
        *core_coil,
        box("heatOutputBackplate", [19.7, 7.55, -0.45], [28.3, 13.25, 0.65], "#dark"),
        box("heatOutputDuctCore", [21.15, 8.1, 0.55], [26.85, 13.7, 15.4], "#dark"),
        *tap_duct,
        box("heatOutputCore", [20.3, 7.8, -0.75], [27.7, 12.8, -0.35], "#heat"),
        box("heatOutputFrameTop", [19.2, 13.25, -0.62], [28.8, 14.0, 0.45], "#copper"),
        box("heatOutputFrameBottom", [19.2, 6.8, -0.62], [28.8, 7.55, 0.45], "#copper"),
        box("heatOutputFrameWest", [19.05, 7.55, -0.62], [19.8, 13.25, 0.45], "#copper"),
        box("heatOutputFrameEast", [28.2, 7.55, -0.62], [28.95, 13.25, 0.45], "#copper"),
        *heat_port_fins,
        parent("ShaftStub", [24, 24, 24], shaft_children),
        box("upperCrossBeamNorth", [8, 28, 6], [40, 31, 10], "#steel"),
        box("upperCrossBeamSouth", [8, 28, 38], [40, 31, 42], "#steel"),
        box("upperCrossBeamWest", [6, 28, 10], [10, 31, 38], "#steel"),
        box("upperCrossBeamEast", [38, 28, 10], [42, 31, 38], "#steel"),
        box("topCap", [16, 31, 16], [32, 32, 32], "#brass"),
    ]
    return shape_document(textures=TEXTURES, elements=elements, texture_width=64, texture_height=64)


def main() -> None:
    write_json(ENGINE_OUT, steam_engine_shape())
    write_json(BORE_OUT, geothermal_bore_shape())
    print(ENGINE_OUT)
    print(BORE_OUT)


if __name__ == "__main__":
    main()
