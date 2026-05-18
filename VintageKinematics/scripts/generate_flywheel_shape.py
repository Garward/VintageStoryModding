#!/usr/bin/env python3
"""Generate the flywheel JSON shape.

The wheel uses the same modeling trick as the treadwheel: repeated rectangular
segments rotated around one shared origin.
"""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUT_SMALL = ROOT / "assets/vintagekinematics/shapes/block/flywheel.json"
OUT_REINFORCED = ROOT / "assets/vintagekinematics/shapes/block/reinforcedflywheel.json"
SMALL_ORIGIN = [8, 8, 8]
REINFORCED_ORIGIN = [8, 24, 8]

# The rim is built from rotated rectangular prisms. If each prism is wider than
# its angular cell, neighboring faces overlap and flicker in game. These spans
# keep the same segment count while leaving a narrow non-coplanar seam.
RIM_FACE_INSET = 0.01
OUTER_RIM_FROM = [6.25 + RIM_FACE_INSET, 16.25, 6.15]
OUTER_RIM_TO = [9.75 - RIM_FACE_INSET, 18.95, 9.85]
INNER_RIM_FROM = [6.75, 14.55, 6.65]
INNER_RIM_TO = [9.25, 16.45, 9.35]
SPOKE_FACE_INSET = 0.01
SPOKE_HUB_CLEARANCE = 0.03
SPOKE_FROM = [6.75 + SPOKE_FACE_INSET, 11 + SPOKE_HUB_CLEARANCE, 7.15]
SPOKE_TO = [9.25 - SPOKE_FACE_INSET, 14.85, 8.85]


def faces(texture: str, uv: list[float] | None = None) -> dict[str, dict[str, object]]:
    uv = uv or [0, 0, 16, 16]
    return {
        side: {"texture": texture, "uv": uv}
        for side in ("north", "south", "east", "west", "up", "down")
    }


def elem(
    name: str,
    from_: list[float],
    to: list[float],
    texture: str,
    rotation_x: float | None = None,
    origin: list[float] | None = None,
) -> dict[str, object]:
    data: dict[str, object] = {
        "name": name,
        "from": from_,
        "to": to,
        "faces": faces(texture),
    }
    if rotation_x is not None:
        data["rotationOrigin"] = origin or SMALL_ORIGIN
        data["rotationX"] = rotation_x
    return data


def make_shape(children: list[dict[str, object]], origin: list[float], darkwood: str) -> dict[str, object]:
    return {
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": {
            "wood": "game:block/wood/planks/generic",
            "darkwood": darkwood,
            "metal": "game:block/metal/sheet/iron1",
        },
        "elements": [
            {
                "name": "flywheelAssembly",
                "from": [0, 0, 0],
                "to": [0, 0, 0],
                "rotationOrigin": origin,
                "faces": {},
                "children": children,
            }
        ],
    }


def build_small() -> list[dict[str, object]]:
    children: list[dict[str, object]] = []

    children.append(elem("inputShaftStub", [0, 6.5, 6.5], [8, 9.5, 9.5], "#darkwood"))
    children.append(elem("outputShaftStub", [8, 6.5, 6.5], [16, 9.5, 9.5], "#wood"))
    children.append(elem("hub", [5, 5, 5], [11, 11, 11], "#metal"))

    for i in range(8):
        angle = i * 45
        children.append(elem(f"spoke{i:02}", SPOKE_FROM, SPOKE_TO, "#wood", angle, SMALL_ORIGIN))

    for i in range(16):
        angle = i * 22.5
        children.append(elem(f"rimSegment{i:02}", OUTER_RIM_FROM, OUTER_RIM_TO, "#wood", angle, SMALL_ORIGIN))
        children.append(elem(f"innerWoodSegment{i:02}", INNER_RIM_FROM, INNER_RIM_TO, "#wood", angle, SMALL_ORIGIN))

    return children


def build_reinforced() -> list[dict[str, object]]:
    children: list[dict[str, object]] = []

    children.append(elem("inputShaftStub", [0, 21.5, 5.5], [8, 26.5, 10.5], "#darkwood"))
    children.append(elem("outputShaftStub", [8, 21.5, 5.5], [16, 26.5, 10.5], "#metal"))
    children.append(elem("hub", [3.5, 19.5, 3.5], [12.5, 28.5, 12.5], "#metal"))
    children.append(elem("hubWoodCore", [5.25, 21.25, 5.25], [10.75, 26.75, 10.75], "#darkwood"))

    for i in range(12):
        angle = i * 30
        children.append(
            elem(
                f"reinforcedSpoke{i:02}",
                [6.25 + SPOKE_FACE_INSET, 28.5 + SPOKE_HUB_CLEARANCE, 6.65],
                [9.75 - SPOKE_FACE_INSET, 39.35, 9.35],
                "#darkwood",
                angle,
                REINFORCED_ORIGIN,
            )
        )

    for i in range(32):
        angle = i * 11.25
        children.append(
            elem(
                f"reinforcedOuterRim{i:02}",
                [5.55 + RIM_FACE_INSET, 41.7, 6.1],
                [10.45 - RIM_FACE_INSET, 46.2, 9.9],
                "#metal",
                angle,
                REINFORCED_ORIGIN,
            )
        )
        children.append(
            elem(
                f"reinforcedOuterSideA{i:02}",
                [4.75, 41.45, 6.2],
                [5.62, 46.5, 9.8],
                "#metal",
                angle,
                REINFORCED_ORIGIN,
            )
        )
        children.append(
            elem(
                f"reinforcedOuterSideB{i:02}",
                [10.38, 41.45, 6.2],
                [11.25, 46.5, 9.8],
                "#metal",
                angle,
                REINFORCED_ORIGIN,
            )
        )
        children.append(
            elem(
                f"reinforcedInnerWood{i:02}",
                [6.1, 38.85, 6.45],
                [9.9, 41.95, 9.55],
                "#darkwood",
                angle,
                REINFORCED_ORIGIN,
            )
        )

    return children


for out, origin, darkwood, children in (
    (OUT_SMALL, SMALL_ORIGIN, "game:block/wood/oak-dark", build_small()),
    (OUT_REINFORCED, REINFORCED_ORIGIN, "game:block/wood/debarked/oak", build_reinforced()),
):
    out.write_text(json.dumps(make_shape(children, origin, darkwood), indent=4) + "\n", encoding="utf-8")
    print(out)
