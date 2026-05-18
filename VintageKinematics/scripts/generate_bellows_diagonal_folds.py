#!/usr/bin/env python3
"""Generate mirrored diagonal bellows folds and matching animation JSON."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
SHAPE_PATH = ROOT / "assets/vintagekinematics/shapes/block/kineticbellows.json"
BLOCKTYPE_PATH = ROOT / "assets/vintagekinematics/blocktypes/kineticbellows.json"
TOP_TRAVEL = 2.45

OLD_FOLD_NAMES = {
    "LeftFold",
    "RightFold",
    "LeftFoldBottom",
    "RightFoldBottom",
    "LeftFoldTop",
    "RightFoldTop",
}

DEFAULT_BOTTOM_Y = 6.0
DEFAULT_TOP_Y = 9.0
BOTTOM_Z = 8.5
TOP_Z = 8.5
FRONT_Z_A = 5.95
FRONT_Z_B = 5.05
CORNER_CAP_INSET = 0.25
FOLD_SPECS = [
    ("LowerA", 0.14),
    ("LowerB", 0.18),
    ("MiddleA", 0.22),
    ("MiddleB", 0.28),
    ("UpperA", 0.34),
    ("UpperB", 0.38),
    ("TopA", 0.42),
    ("TopB", 0.46),
]
SIDE_FOLD_SPECS = FOLD_SPECS
FRONT_FOLD_SPECS = SIDE_FOLD_SPECS
CORNER_CAP_SPECS = [
    ("Lower", 0),
    ("MiddleLower", 2),
    ("MiddleUpper", 4),
    ("Upper", 6),
]
FOLD_NAMES = [
    f"{side}FoldDiag{suffix}"
    for side in ("Left", "Right")
    for suffix, *_ in SIDE_FOLD_SPECS
] + [
    f"{kind}{suffix}"
    for kind in ("FrontFold", "LeftFoldCorner", "RightFoldCorner")
    for suffix, *_ in FRONT_FOLD_SPECS
] + [
    f"{kind}{suffix}"
    for kind in ("LeftFoldCornerCap", "RightFoldCornerCap")
    for suffix, *_ in CORNER_CAP_SPECS
]
ANCHOR_NAMES = {
    "LeftFoldBottomAnchor",
    "RightFoldBottomAnchor",
    "LeftFoldFrontBottomAnchor",
    "RightFoldFrontBottomAnchor",
    "FrontFoldBottomAnchor",
}


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as file:
        return json.load(file)


def write_json(path: Path, data: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8") as file:
        json.dump(data, file, indent="\t")
        file.write("\n")


def round_coord(value: float) -> float:
    return round(value, 4)


def find_element(elements: list[dict[str, Any]], name: str) -> dict[str, Any] | None:
    for element in elements:
        if element.get("name") == name:
            return element
        children = element.get("children")
        if isinstance(children, list):
            found = find_element(children, name)
            if found is not None:
                return found
    return None


def bag_y_bounds(shape: dict[str, Any]) -> tuple[float, float]:
    bag_bottom = find_element(shape.get("elements", []), "BagBottom")
    bag_top = find_element(shape.get("elements", []), "BagTop")

    bottom_y = DEFAULT_BOTTOM_Y
    top_y = DEFAULT_TOP_Y
    if isinstance(bag_bottom, dict):
        to = bag_bottom.get("to")
        if isinstance(to, list) and len(to) > 1:
            bottom_y = float(to[1])
    if isinstance(bag_top, dict):
        from_ = bag_top.get("from")
        if isinstance(from_, list) and len(from_) > 1:
            top_y = float(from_[1])

    return bottom_y, top_y


def leather_faces(uv_x1: float, uv_y1: float, uv_x2: float, uv_y2: float) -> dict[str, Any]:
    main_uv = [round_coord(uv_x1), round_coord(uv_y1), round_coord(uv_x2), round_coord(uv_y2)]
    edge_uv = [round_coord(uv_x1), round_coord(uv_y1), round_coord(uv_x2), round_coord(uv_y1 + 0.25)]
    return {
        "north": {"texture": "#leather", "uv": main_uv},
        "south": {"texture": "#leather", "uv": main_uv},
        "east": {"texture": "#leather", "uv": edge_uv},
        "west": {"texture": "#leather", "uv": edge_uv},
        "up": {"texture": "#leather", "uv": main_uv},
        "down": {"texture": "#leather", "uv": main_uv},
    }


def fold_element(name: str, x1: float, x2: float, hinge_x: float, y: float, z_pad: float) -> dict[str, Any]:
    thickness = 0.08
    z1 = 5.36 + z_pad
    z2 = 12.0 - z_pad
    return {
        "name": name,
        "from": [round_coord(x1), round_coord(y - thickness / 2), round_coord(z1)],
        "to": [round_coord(x2), round_coord(y + thickness / 2), round_coord(z2)],
        "rotationOrigin": [round_coord(hinge_x), round_coord(y), 8.5],
        "faces": leather_faces(0.0, 6.0, 7.0, 7.0),
    }


def front_fold_element(
    name: str,
    x1: float,
    x2: float,
    hinge_z: float,
    y: float,
    children: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    thickness = 0.08
    z1 = min(FRONT_Z_A, FRONT_Z_B)
    z2 = max(FRONT_Z_A, FRONT_Z_B)
    element = {
        "name": name,
        "from": [round_coord(x1), round_coord(y - thickness / 2), round_coord(z1)],
        "to": [round_coord(x2), round_coord(y + thickness / 2), round_coord(z2)],
        "rotationOrigin": [round_coord((x1 + x2) / 2), round_coord(y), round_coord(hinge_z)],
        "faces": leather_faces(0.0, 6.0, round_coord(x2 - x1), 7.0),
    }
    if children:
        element["children"] = children
    return element


def corner_cap_element(name: str, side: str, parent_x1: float, y: float, hinge_z: float) -> dict[str, Any]:
    rotation_y = -38.0 if side == "left" else 38.0
    rotation_z = 43.0 if side == "left" else -43.0
    x_inset = CORNER_CAP_INSET if side == "left" else -CORNER_CAP_INSET
    z1 = min(FRONT_Z_A, FRONT_Z_B) + CORNER_CAP_INSET
    return {
        "name": name,
        "from": [round_coord(parent_x1 + x_inset - 0.18), round_coord(y - 0.08), round_coord(z1 + 0.42)],
        "to": [round_coord(parent_x1 + x_inset + 0.82), round_coord(y + 0.92), round_coord(z1 + 1.42)],
        "rotationOrigin": [round_coord(parent_x1 + x_inset + 0.32), round_coord(y + 0.42), round_coord(z1 + 0.92)],
        "rotationX": 0.0,
        "rotationY": rotation_y,
        "rotationZ": rotation_z,
        "faces": leather_faces(0.0, 6.0, 1.0, 7.0),
    }


def anchor_element(name: str, x1: float, x2: float, y1: float, y2: float, z_pad: float) -> dict[str, Any]:
    return {
        "name": name,
        "from": [round_coord(x1), round_coord(y1), round_coord(5.0 + z_pad)],
        "to": [round_coord(x2), round_coord(y2), round_coord(12.0 - z_pad)],
        "faces": leather_faces(0.0, 6.0, 7.0, 6.35),
    }


def front_anchor_element(name: str, x1: float, x2: float, y1: float, y2: float) -> dict[str, Any]:
    return {
        "name": name,
        "from": [round_coord(x1), round_coord(y1), min(FRONT_Z_A, FRONT_Z_B)],
        "to": [round_coord(x2), round_coord(y2), max(FRONT_Z_A, FRONT_Z_B)],
        "faces": leather_faces(0.0, 6.0, round_coord(x2 - x1), 6.35),
    }


def generated_folds(bottom_y: float, top_y: float) -> list[dict[str, Any]]:
    anchor_y1 = bottom_y - 0.04
    anchor_y2 = bottom_y + 0.12
    folds: list[dict[str, Any]] = [
        anchor_element("LeftFoldBottomAnchor", 2.85, 3.12, anchor_y1, anchor_y2, 0.14),
        anchor_element("RightFoldBottomAnchor", 12.88, 13.15, anchor_y1, anchor_y2, 0.14),
        front_anchor_element("FrontFoldBottomAnchor", 3.05, 12.95, anchor_y1, anchor_y2),
    ]
    side_step = (top_y - bottom_y) / len(SIDE_FOLD_SPECS)
    front_step = (top_y - bottom_y) / len(FRONT_FOLD_SPECS)

    for index, (suffix, z_pad) in enumerate(SIDE_FOLD_SPECS):
        y = bottom_y + side_step * index
        left_hinge_x = 3.05 if index % 2 == 0 else 1.9
        folds.append(fold_element(f"LeftFoldDiag{suffix}", 1.9, 3.05, left_hinge_x, y, z_pad))

        right_hinge_x = 12.95 if index % 2 == 0 else 14.1
        folds.append(fold_element(f"RightFoldDiag{suffix}", 12.95, 14.1, right_hinge_x, y, z_pad))

    for index, (suffix, _z_pad) in enumerate(FRONT_FOLD_SPECS):
        y = bottom_y + front_step * index
        front_hinge_z = FRONT_Z_A if index % 2 == 0 else FRONT_Z_B
        folds.append(front_fold_element(f"FrontFold{suffix}", 3.05, 12.95, front_hinge_z, y))
        folds.append(front_fold_element(f"LeftFoldCorner{suffix}", 2.65, 3.25, front_hinge_z, y))
        folds.append(front_fold_element(f"RightFoldCorner{suffix}", 12.75, 13.35, front_hinge_z, y))

    for suffix, pair_start_index in CORNER_CAP_SPECS:
        y = bottom_y + front_step * (pair_start_index + 1.0) - 0.5
        folds.append(corner_cap_element(f"LeftFoldCornerCap{suffix}", "left", 2.65, y, FRONT_Z_A))
        folds.append(corner_cap_element(f"RightFoldCornerCap{suffix}", "right", 12.75, y, FRONT_Z_A))

    return folds


def remove_named_elements(elements: list[dict[str, Any]], names: set[str]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for element in elements:
        name = element.get("name")
        if (
            name in names
            or name in ANCHOR_NAMES
            or (
                isinstance(name, str)
                and (
                    name.startswith("LeftFoldDiag")
                    or name.startswith("RightFoldDiag")
                    or name.startswith("LeftFoldFront")
                    or name.startswith("RightFoldFront")
                    or name.startswith("FrontFold")
                    or name.startswith("LeftFoldCorner")
                    or name.startswith("RightFoldCorner")
                    or name.startswith("LeftFoldCornerCap")
                    or name.startswith("RightFoldCornerCap")
                    or name.startswith("LeftFoldBottomAnchor")
                    or name.startswith("RightFoldBottomAnchor")
                )
            )
        ):
            continue
        if isinstance(element.get("children"), list):
            element["children"] = remove_named_elements(element["children"], names)
        result.append(element)
    return result


def insert_after(elements: list[dict[str, Any]], after_name: str, new_elements: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    inserted = False
    for element in elements:
        result.append(element)
        if element.get("name") == after_name:
            result.extend(new_elements)
            inserted = True
    if not inserted:
        result.extend(new_elements)
    return result


def update_shape() -> None:
    shape = load_json(SHAPE_PATH)
    bottom_y, top_y = bag_y_bounds(shape)
    existing_names = OLD_FOLD_NAMES | set(FOLD_NAMES)
    elements = remove_named_elements(shape["elements"], existing_names)
    shape["elements"] = insert_after(elements, "BagTop", generated_folds(bottom_y, top_y))
    write_json(SHAPE_PATH, shape)


def linked_pleat_behavior(bottom_y: float, top_y: float) -> dict[str, Any]:
    suffixes = [suffix for suffix, _z_pad in FRONT_FOLD_SPECS]
    side_suffixes = [suffix for suffix, _z_pad in SIDE_FOLD_SPECS]
    cap_suffixes = [suffix for suffix, _pair_start_index in CORNER_CAP_SPECS]
    cap_common = {
        "translateOnly": True,
        "translateTOffset": 1.0 / len(FRONT_FOLD_SPECS),
        "translateTStep": 2.0 / len(FRONT_FOLD_SPECS),
    }
    left_common = {
        "plane": "xy",
        "waveform": "sine",
        "ratio": 1,
        "bottom": {"x": 1.9, "y": bottom_y, "z": BOTTOM_Z},
        "top": {"x": 3.05, "y": top_y, "z": TOP_Z},
        "topTravelY": -TOP_TRAVEL,
        "xA": 1.9,
        "xB": 3.05,
        "startAtA": False,
    }
    right_common = {
        "plane": "xy",
        "waveform": "sine",
        "ratio": 1,
        "bottom": {"x": 14.1, "y": bottom_y, "z": BOTTOM_Z},
        "top": {"x": 12.95, "y": top_y, "z": TOP_Z},
        "topTravelY": -TOP_TRAVEL,
        "xA": 14.1,
        "xB": 12.95,
        "startAtA": False,
    }
    front_common = {
        "plane": "zy",
        "waveform": "sine",
        "ratio": 1,
        "bottom": {"x": 8.0, "y": bottom_y, "z": FRONT_Z_A},
        "top": {"x": 8.0, "y": top_y, "z": FRONT_Z_B},
        "topTravelY": -TOP_TRAVEL,
        "zA": FRONT_Z_A,
        "zB": FRONT_Z_B,
        "startAtA": True,
    }
    left_corner = {
        **front_common,
        "bottom": {"x": 2.95, "y": bottom_y, "z": FRONT_Z_A},
        "top": {"x": 2.95, "y": top_y, "z": FRONT_Z_B},
    }
    right_corner = {
        **front_common,
        "bottom": {"x": 13.05, "y": bottom_y, "z": FRONT_Z_A},
        "top": {"x": 13.05, "y": top_y, "z": FRONT_Z_B},
    }
    return {
        "name": "KineticLinkedPleat",
        "properties": {
            "chains": [
                {
                    "elements": [f"LeftFoldDiag{suffix}" for suffix in side_suffixes],
                    **left_common,
                },
                {
                    "elements": [f"FrontFold{suffix}" for suffix in suffixes],
                    **front_common,
                },
                {
                    "elements": [f"RightFoldDiag{suffix}" for suffix in side_suffixes],
                    **right_common,
                },
                {
                    "elements": [f"LeftFoldCorner{suffix}" for suffix in suffixes],
                    **left_corner,
                },
                {
                    "elements": [f"LeftFoldCornerCap{suffix}" for suffix in cap_suffixes],
                    **cap_common,
                    **left_corner,
                },
                {
                    "elements": [f"RightFoldCorner{suffix}" for suffix in suffixes],
                    **right_corner,
                },
                {
                    "elements": [f"RightFoldCornerCap{suffix}" for suffix in cap_suffixes],
                    **cap_common,
                    **right_corner,
                },
            ]
        },
    }


def update_blocktype() -> None:
    blocktype = load_json(BLOCKTYPE_PATH)
    bottom_y, top_y = bag_y_bounds(load_json(SHAPE_PATH))
    for behaviors in blocktype["entityBehaviorsByType"].values():
        behaviors[:] = [behavior for behavior in behaviors if behavior.get("name") != "KineticStretch"]
        behaviors[:] = [behavior for behavior in behaviors if behavior.get("name") != "KineticLinkedPleat"]

        animator = next((behavior for behavior in behaviors if behavior.get("name") == "KineticAnimator"), None)
        if animator is None:
            animator = {"name": "KineticAnimator", "properties": {"rotators": []}}
            behaviors.insert(1, animator)

        animator["properties"]["rotators"] = [
            {"element": "ShaftStub", "axis": "Z", "ratio": 1},
        ]
        behaviors.insert(2, linked_pleat_behavior(bottom_y, top_y))

        piston = next((behavior for behavior in behaviors if behavior.get("name") == "KineticPiston"), None)
        if piston is not None:
            pistons = []
            for piston_def in piston["properties"]["pistons"]:
                if piston_def.get("element") in {"LeftFoldTop", "RightFoldTop"}:
                    continue
                if piston_def.get("element") in {"TopBoard", "BagTop", "Handle"}:
                    piston_def["travel"] = TOP_TRAVEL
                pistons.append(piston_def)
            piston["properties"]["pistons"] = pistons

    write_json(BLOCKTYPE_PATH, blocktype)


def main() -> None:
    update_shape()
    update_blocktype()
    print(f"Generated {len(FOLD_NAMES)} diagonal fold elements")


if __name__ == "__main__":
    main()
