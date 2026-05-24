#!/usr/bin/env python3
"""Generate flywheel backpack shapes from the vanilla sturdy backpack plus a scaled flywheel."""

from __future__ import annotations

import copy
import json
import os
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
VS_ROOT = Path(os.environ.get("VINTAGE_STORY", "/home/garward/Games/Games/VintageStory/vintagestory"))
VANILLA_SHAPE_DIR = VS_ROOT / "assets/survival/shapes/item/bag"
FLYWHEEL_SHAPE_PATH = ROOT / "assets/vintagekinematics/shapes/block/flywheel.json"
OUT_DIR = ROOT / "assets/vintagekinematics/shapes/item/bag"
BLOCK_OUT_DIR = ROOT / "assets/vintagekinematics/shapes/block"

ITEM_SCALE = 0.19
ATTACHED_SCALE = 0.36
PLACED_SCALE = 0.36
ITEM_FLYWHEEL_CENTER = [8.0, 7.25, 10.72]
ATTACHED_FLYWHEEL_CENTER = [1.2, 1.25, 2.72]
PLACED_FLYWHEEL_CENTER = [8.0, 8.25, 11.72]
FLYWHEEL_SOURCE_CENTER = [8.0, 8.0, 8.0]
SHAFT_HALF_WIDTH = 0.26
ITEM_CONNECTOR_Z = [9.85, 11.82]
ATTACHED_CONNECTOR_Z = [2.05, 4.35]
PLACED_CONNECTOR_Z = [10.85, 13.9]


def transform_point(point: list[float], center: list[float], scale: float) -> list[float]:
    x, y, z = point
    return [
        round(center[0] + (z - FLYWHEEL_SOURCE_CENTER[2]) * scale, 3),
        round(center[1] + (y - FLYWHEEL_SOURCE_CENTER[1]) * scale, 3),
        round(center[2] + (x - FLYWHEEL_SOURCE_CENTER[0]) * scale, 3),
    ]


def transform_element(element: dict[str, Any], center: list[float], scale: float) -> dict[str, Any] | None:
    if element.get("name") in {"inputShaftStub", "outputShaftStub"}:
        return None

    transformed = copy.deepcopy(element)
    transformed["name"] = f"backpackFlywheel_{element.get('name', 'part')}"
    transformed["from"] = transform_point(element["from"], center, scale)
    transformed["to"] = transform_point(element["to"], center, scale)
    transformed["from"], transformed["to"] = [
        [min(a, b) for a, b in zip(transformed["from"], transformed["to"])],
        [max(a, b) for a, b in zip(transformed["from"], transformed["to"])],
    ]

    if "rotationOrigin" in transformed:
        transformed["rotationOrigin"] = transform_point(transformed["rotationOrigin"], center, scale)
    if "origin" in transformed:
        transformed["origin"] = transform_point(transformed["origin"], center, scale)
    rotations = {
        "rotationX": transformed.pop("rotationX", None),
        "rotationY": transformed.pop("rotationY", None),
        "rotationZ": transformed.pop("rotationZ", None),
    }
    if rotations["rotationX"] is not None:
        transformed["rotationZ"] = rotations["rotationX"]
    if rotations["rotationY"] is not None:
        transformed["rotationY"] = rotations["rotationY"]
    if rotations["rotationZ"] is not None:
        transformed["rotationX"] = rotations["rotationZ"]

    return transformed


def iter_flywheel_parts(flywheel_shape: dict[str, Any]) -> list[dict[str, Any]]:
    assembly = next(element for element in flywheel_shape["elements"] if element.get("name") == "flywheelAssembly")
    return assembly.get("children", [])


def flywheel_elements(flywheel_shape: dict[str, Any], center: list[float], scale: float) -> list[dict[str, Any]]:
    return [
        transformed
        for element in iter_flywheel_parts(flywheel_shape)
        if (transformed := transform_element(element, center, scale)) is not None
    ]


def shaft_element(name: str, center: list[float], from_z: float, to_z: float) -> dict[str, Any]:
    return {
        "name": name,
        "from": [round(center[0] - SHAFT_HALF_WIDTH, 3), round(center[1] - SHAFT_HALF_WIDTH, 3), from_z],
        "to": [round(center[0] + SHAFT_HALF_WIDTH, 3), round(center[1] + SHAFT_HALF_WIDTH, 3), to_z],
        "rotationOrigin": center,
        "faces": {
            "north": {"texture": "#metal", "uv": [7.0, 7.0, 8.0, 8.0]},
            "east": {"texture": "#metal", "uv": [4.0, 5.0, 10.0, 6.0]},
            "south": {"texture": "#metal", "uv": [7.0, 7.0, 8.0, 8.0]},
            "west": {"texture": "#metal", "uv": [4.0, 5.0, 10.0, 6.0]},
            "up": {"texture": "#metal", "uv": [4.0, 5.0, 10.0, 6.0]},
            "down": {"texture": "#metal", "uv": [4.0, 5.0, 10.0, 6.0]},
        },
    }


def add_flywheel_to_backpack(shape: dict[str, Any], flywheel_shape: dict[str, Any], *, attached: bool = False, placed: bool = False) -> dict[str, Any]:
    shape = copy.deepcopy(shape)
    textures = shape.setdefault("textures", {})
    textures["wood"] = "block/wood/planks/generic"
    textures["darkwood"] = "block/wood/oak-dark"
    textures["metal"] = "block/metal/sheet/brass2"

    backpack = next(element for element in shape["elements"] if element.get("name") == "backpack")
    children = backpack.setdefault("children", [])
    children[:] = [
        child
        for child in children
        if child.get("name") != "flywheelAssembly"
        and not child.get("name", "").startswith("backpackFlywheel_")
    ]
    shape["elements"] = [
        element
        for element in shape["elements"]
        if element.get("name") != "flywheelAssembly"
    ]

    if placed:
        center = PLACED_FLYWHEEL_CENTER
        scale = PLACED_SCALE
        from_z, to_z = PLACED_CONNECTOR_Z
    elif attached:
        center = ATTACHED_FLYWHEEL_CENTER
        scale = ATTACHED_SCALE
        from_z, to_z = ATTACHED_CONNECTOR_Z
    else:
        center = ITEM_FLYWHEEL_CENTER
        scale = ITEM_SCALE
        from_z, to_z = ITEM_CONNECTOR_Z

    child_center = center if attached else [0.0, 0.0, 0.0]
    child_from_z = from_z if attached else round(from_z - center[2], 3)
    child_to_z = to_z if attached else round(to_z - center[2], 3)

    assembly = {
        "name": "flywheelAssembly",
        "from": center,
        "to": center,
        "rotationOrigin": center,
        "faces": disabled_faces(),
        "children": flywheel_elements(flywheel_shape, child_center, scale) + [shaft_element("flywheelShaft", child_center, child_from_z, child_to_z)],
    }
    if attached:
        children.append(assembly)
    else:
        shape["elements"].append(assembly)
    return shape


def disabled_faces() -> dict[str, dict[str, Any]]:
    return {
        face: {"texture": "#null", "uv": [0, 0, 0.5, 0.5], "enabled": False}
        for face in ("north", "east", "south", "west", "up", "down")
    }


def write_shape(source_name: str, out_name: str, flywheel_shape: dict[str, Any]) -> None:
    source = json.loads((VANILLA_SHAPE_DIR / source_name).read_text())
    output = add_flywheel_to_backpack(source, flywheel_shape, attached=source_name.endswith("-attached.json"))
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    (OUT_DIR / out_name).write_text(json.dumps(output, indent="\t") + "\n")


def write_block_shape(flywheel_shape: dict[str, Any]) -> None:
    source = json.loads((VANILLA_SHAPE_DIR / "backpack-sturdy.json").read_text())
    output = add_flywheel_to_backpack(source, flywheel_shape, placed=True)
    BLOCK_OUT_DIR.mkdir(parents=True, exist_ok=True)
    (BLOCK_OUT_DIR / "backpackflywheelplaced.json").write_text(json.dumps(output, indent="\t") + "\n")


def main() -> None:
    flywheel_shape = json.loads(FLYWHEEL_SHAPE_PATH.read_text())
    write_shape("backpack-sturdy.json", "backpackflywheel.json", flywheel_shape)
    write_shape("backpack-sturdy-attached.json", "backpackflywheel-attached.json", flywheel_shape)
    write_block_shape(flywheel_shape)


if __name__ == "__main__":
    main()
