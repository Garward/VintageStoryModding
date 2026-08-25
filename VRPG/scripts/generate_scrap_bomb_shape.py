#!/usr/bin/env python3
"""Generate the rounded, riveted projectile used by the Scrap Bomb skill."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/vrpg/shapes/entity/skill/scrap-bomb.json"
CENTER = [8, 8, 8]
SIDES = ("north", "east", "south", "west", "up", "down")
COPLANAR_OFFSET = 0.01


def offset_for(index: int) -> float:
    """Stagger neighboring rotated plates so overlapping caps never share a plane."""
    if index == 0:
        return 0.0
    step = (index + 1) // 2
    sign = 1 if index % 2 else -1
    return sign * step * COPLANAR_OFFSET


def num(value: float) -> float:
    return round(value, 4)


def faces(texture: str) -> dict[str, dict[str, Any]]:
    return {side: {"texture": texture, "uv": [0, 0, 16, 16]} for side in SIDES}


def box(
    name: str,
    from_: list[float],
    to: list[float],
    texture: str,
    *,
    rotation_y: float | None = None,
    origin: list[float] | None = None,
) -> dict[str, Any]:
    element: dict[str, Any] = {
        "name": name,
        "from": from_,
        "to": to,
        "faces": faces(texture),
    }
    if rotation_y is not None:
        element["rotationOrigin"] = origin or CENTER
        element["rotationY"] = rotation_y
    return element


def ring(
    name: str,
    y1: float,
    y2: float,
    radius: float,
    width: float,
    texture: str,
    segments: int = 16,
) -> list[dict[str, Any]]:
    return [
        box(
            f"{name}-{index:02d}",
            [num(8 - width / 2), num(y1 + offset_for(index)), num(8 + radius - 0.42)],
            [num(8 + width / 2), num(y2 + offset_for(index)), num(8 + radius + 0.42)],
            texture,
            rotation_y=index * 360 / segments,
        )
        for index in range(segments)
    ]


def radial_cap(
    name: str,
    y1: float,
    y2: float,
    inner_radius: float,
    outer_radius: float,
    width: float,
    texture: str,
    segments: int = 16,
) -> list[dict[str, Any]]:
    """Build a shallow fan that closes the gap between the core and shell."""
    return [
        box(
            f"{name}-{index:02d}",
            [num(8 - width / 2), num(y1 + offset_for(index)), num(8 + inner_radius)],
            [num(8 + width / 2), num(y2 + offset_for(index)), num(8 + outer_radius)],
            texture,
            rotation_y=index * 360 / segments,
        )
        for index in range(segments)
    ]


def build() -> dict[str, Any]:
    elements: list[dict[str, Any]] = [
        box("dark-core", [5.2, 5.0, 5.2], [10.8, 11.0, 10.8], "#dark"),
        box("bottom-cap", [5.8, 3.8, 5.8], [10.2, 5.2, 10.2], "#rust"),
        box("top-cap", [5.8, 10.8, 5.8], [10.2, 12.2, 10.2], "#rust"),
    ]
    elements.extend(ring("lower-shell", 4.4, 6.7, 4.25, 2.65, "#rust"))
    elements.extend(ring("middle-shell", 6.55, 9.45, 4.9, 2.75, "#rust"))
    elements.extend(ring("upper-shell", 9.3, 11.6, 4.25, 2.65, "#rust"))
    elements.extend(radial_cap("bottom-shoulder", 4.02, 4.32, 2.0, 4.1, 2.65, "#rust"))
    elements.extend(radial_cap("top-shoulder", 11.08, 11.38, 2.0, 4.1, 2.65, "#rust"))
    elements.extend(ring("lower-copper-band", 6.35, 6.85, 4.7, 2.85, "#copper"))
    elements.extend(ring("upper-copper-band", 9.15, 9.65, 4.7, 2.85, "#copper"))

    for index in range(8):
        angle = index * 45
        elements.append(
            box(
                f"rivet-lower-{index:02d}",
                [7.67, 6.05, 12.55],
                [8.33, 6.71, 13.18],
                "#copper",
                rotation_y=angle,
            )
        )
        elements.append(
            box(
                f"rivet-upper-{index:02d}",
                [7.67, 9.52, 12.55],
                [8.33, 10.18, 13.18],
                "#copper",
                rotation_y=angle + 22.5,
            )
        )

    elements.extend(ring("fuse-collar", 11.8, 13.2, 1.35, 0.9, "#copper", segments=8))
    elements.append(box("fuse", [7.72, 12.7, 7.72], [8.28, 16.0, 8.28], "#fuse"))
    elements.append(
        {
            **box("fuse-crook", [7.72, 15.2, 7.72], [8.28, 17.2, 8.28], "#fuse"),
            "rotationOrigin": [8, 15.2, 8],
            "rotationZ": -35,
        }
    )

    return {
        "editor": {"allAngles": True, "entityTextureMode": False},
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": {
            "rust": "game:block/metal/tarnished/rusty-iron",
            "dark": "game:block/metal/sheet/iron1",
            "copper": "game:block/metal/plate/copper",
            "fuse": "game:item/resource/crushed/blastingpowder",
        },
        "elements": elements,
    }


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(build(), indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
