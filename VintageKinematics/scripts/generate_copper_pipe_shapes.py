#!/usr/bin/env python3
"""Generate canonical copper pipe block shapes and blocktype variants.

The authored set stays intentionally small: one straight, one elbow, one tee,
and three cross pieces. Rotation/connection logic can reuse these canonical
models later instead of maintaining a file for every possible face mask.

Every pipe arm is a 6x6 rounded tube and runs exactly to block boundaries.
Elbows follow the vanilla chute pattern: short outlet sections plus a separate
mostly solid corner fitting, while the exposed pipe outlets stay round.
Tee and cross intersections use a central fitting that hides the internal pipe
geometry instead of trying to boolean-cut the tube walls.
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

from shapegen import coords, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
SHAPE_DIR = ROOT / "assets/vintagekinematics/shapes/block/copperpipe"
BLOCKTYPE_OUT = ROOT / "assets/vintagekinematics/blocktypes/copperpipe.json"
PUMP_BLOCKTYPE_OUT = ROOT / "assets/vintagekinematics/blocktypes/copperpump.json"
COGWHEEL_SHAPE_IN = ROOT / "assets/vintagekinematics/shapes/block/cogwheel.json"

TEXTURES = {
    "copper": "game:block/metal/plate/copper",
    "dark": "game:block/metal/plate/tinbronze",
    "cog": "game:block/wood/planks/generic",
}

FACE_ORDER = ("n", "e", "s", "w", "u", "d")
HORIZONTAL_FACE_ORDER = ("n", "e", "s", "w")
OPPOSITE_PAIRS = (("n", "s"), ("e", "w"), ("u", "d"))
FACE_VECTORS = {
    "n": (0, 0, -1),
    "e": (1, 0, 0),
    "s": (0, 0, 1),
    "w": (-1, 0, 0),
    "u": (0, 1, 0),
    "d": (0, -1, 0),
}
VECTOR_FACES = {value: key for key, value in FACE_VECTORS.items()}
PIPE_CENTER = 8.0
OUTER_RADIUS = 3.0
INNER_RADIUS = 2.05
STRIP_WIDTH = 1.35
STRIP_COUNT = 16
ELBOW_N_END = 6.0
ELBOW_E_START = 10.0
COPLANAR_OFFSET = 0.01
UV_ATLAS_MARGIN = 0.25


def offset_for(index: int) -> float:
    if index == 0:
        return 0.0
    step = (index + 1) // 2
    sign = 1 if index % 2 else -1
    return sign * step * COPLANAR_OFFSET


def uv_rect(width: float, height: float) -> list[float]:
    max_uv = 16.0 - UV_ATLAS_MARGIN
    return coords(
        [
            UV_ATLAS_MARGIN,
            UV_ATLAS_MARGIN,
            min(max_uv, UV_ATLAS_MARGIN + max(0.1, width)),
            min(max_uv, UV_ATLAS_MARGIN + max(0.1, height)),
        ]
    )


def face_uv(from_: list[float], to: list[float], omit: set[str] | None = None) -> dict[str, dict[str, object]]:
    omit = omit or set()
    x = abs(to[0] - from_[0])
    y = abs(to[1] - from_[1])
    z = abs(to[2] - from_[2])
    faces = {
        "north": {"texture": "#copper", "uv": uv_rect(x, y)},
        "east": {"texture": "#copper", "uv": uv_rect(z, y)},
        "south": {"texture": "#copper", "uv": uv_rect(x, y)},
        "west": {"texture": "#copper", "uv": uv_rect(z, y)},
        "up": {"texture": "#copper", "uv": uv_rect(x, z)},
        "down": {"texture": "#copper", "uv": uv_rect(x, z)},
    }
    for face in omit:
        faces.pop(face, None)
    return faces


def box(
    name: str,
    from_: list[float],
    to: list[float],
    omit_faces: set[str] | None = None,
) -> dict[str, object]:
    return {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": face_uv(from_, to, omit_faces),
    }


def textured(element: dict[str, object], texture: str) -> dict[str, object]:
    for face in element.get("faces", {}).values():
        if isinstance(face, dict):
            face["texture"] = texture
    return element


def rotated_box(
    name: str,
    from_: list[float],
    to: list[float],
    *,
    rotation_origin: list[float],
    rotation_axis: str,
    rotation_degrees: float,
    omit_faces: set[str] | None = None,
) -> dict[str, object]:
    element = box(name, from_, to, omit_faces)
    element["rotationOrigin"] = coords(rotation_origin)
    if rotation_axis == "x":
        element["rotationX"] = round(rotation_degrees, 4)
    elif rotation_axis == "y":
        element["rotationY"] = round(rotation_degrees, 4)
    else:
        element["rotationZ"] = round(rotation_degrees, 4)
    return element


def axial_bounds_for_face(face: str, offset: float) -> tuple[float, float]:
    if face in ("n", "w", "d"):
        return 0.0, min(16.0, PIPE_CENTER + OUTER_RADIUS + offset)
    return max(0.0, PIPE_CENTER - OUTER_RADIUS - offset), 16.0


def axial_bounds_for_pair(pair: tuple[str, str]) -> tuple[float, float]:
    return 0.0, 16.0


def tube_elements(axis: str, start: float, end: float, prefix: str) -> list[dict[str, object]]:
    elements: list[dict[str, object]] = []
    for index in range(STRIP_COUNT):
        angle = index * 360.0 / STRIP_COUNT
        thin = offset_for(index)
        inner = INNER_RADIUS - thin
        outer = OUTER_RADIUS + thin
        half_width = STRIP_WIDTH / 2.0 + thin

        if axis == "z":
            from_ = [PIPE_CENTER - half_width, PIPE_CENTER + inner, start]
            to = [PIPE_CENTER + half_width, PIPE_CENTER + outer, end]
            rotation_axis = "z"
            omit_faces = set()
            if start > 0.0:
                omit_faces.add("north")
            if end < 16.0:
                omit_faces.add("south")
        elif axis == "x":
            from_ = [start, PIPE_CENTER - half_width, PIPE_CENTER + inner]
            to = [end, PIPE_CENTER + half_width, PIPE_CENTER + outer]
            rotation_axis = "x"
            omit_faces = set()
            if start > 0.0:
                omit_faces.add("west")
            if end < 16.0:
                omit_faces.add("east")
        else:
            from_ = [PIPE_CENTER - half_width, start, PIPE_CENTER + inner]
            to = [PIPE_CENTER + half_width, end, PIPE_CENTER + outer]
            rotation_axis = "y"
            omit_faces = set()
            if start > 0.0:
                omit_faces.add("down")
            if end < 16.0:
                omit_faces.add("up")

        elements.append(
            rotated_box(
                f"{prefix}-wall-{index:02d}",
                from_,
                to,
                rotation_origin=[PIPE_CENTER, PIPE_CENTER, PIPE_CENTER],
                rotation_axis=rotation_axis,
                rotation_degrees=angle,
                omit_faces=omit_faces,
            )
        )
    return elements


def solid_round_elements(axis: str, start: float, end: float, prefix: str, radius: float) -> list[dict[str, object]]:
    elements: list[dict[str, object]] = []
    for index in range(STRIP_COUNT):
        angle = index * 360.0 / STRIP_COUNT
        thin = offset_for(index)
        half_width = STRIP_WIDTH / 2.0 + thin

        if axis == "z":
            from_ = [PIPE_CENTER - half_width, PIPE_CENTER - thin, start]
            to = [PIPE_CENTER + half_width, PIPE_CENTER + radius + thin, end]
            rotation_axis = "z"
        elif axis == "x":
            from_ = [start, PIPE_CENTER - half_width, PIPE_CENTER - thin]
            to = [end, PIPE_CENTER + half_width, PIPE_CENTER + radius + thin]
            rotation_axis = "x"
        else:
            from_ = [PIPE_CENTER - half_width, start, PIPE_CENTER - thin]
            to = [PIPE_CENTER + half_width, end, PIPE_CENTER + radius + thin]
            rotation_axis = "y"

        elements.append(
            rotated_box(
                f"{prefix}-solid-{index:02d}",
                from_,
                to,
                rotation_origin=[PIPE_CENTER, PIPE_CENTER, PIPE_CENTER],
                rotation_axis=rotation_axis,
                rotation_degrees=angle,
            )
        )
    return elements


def elbow_ne_elements() -> list[dict[str, object]]:
    elements = []
    elements.extend(tube_elements("z", 0.0, ELBOW_N_END, "pipe-n"))
    elements.extend(tube_elements("x", ELBOW_E_START, 16.0, "pipe-e"))
    elements.extend(elbow_corner_fitting_elements())
    return elements


def elbow_corner_fitting_elements() -> list[dict[str, object]]:
    inset = COPLANAR_OFFSET
    return [
        box("elbow-corner-body", [5.0, 5.0, 3.0], [13.0, 11.0, 11.0]),
        box(
            "elbow-corner-n-neck",
            [5.5, 5.5, ELBOW_N_END - inset],
            [10.5, 10.5, 7.0],
            omit_faces={"north"},
        ),
        box(
            "elbow-corner-e-neck",
            [9.0, 5.5, 5.5],
            [ELBOW_E_START + inset, 10.5, 10.5],
            omit_faces={"east"},
        ),
        box(
            "elbow-corner-top-softener",
            [6.0, 11.0 - inset, 4.0],
            [12.0, 12.0, 10.0],
            omit_faces={"down"},
        ),
        box(
            "elbow-corner-bottom-softener",
            [6.0, 4.0, 4.0],
            [12.0, 5.0 + inset, 10.0],
            omit_faces={"up"},
        ),
    ]


def intersection_fitting_elements() -> list[dict[str, object]]:
    return [
        box(
            "intersection-body",
            [PIPE_CENTER - 3.15, PIPE_CENTER - 3.15, PIPE_CENTER - 3.15],
            [PIPE_CENTER + 3.15, PIPE_CENTER + 3.15, PIPE_CENTER + 3.15],
        ),
        box(
            "intersection-top-face",
            [PIPE_CENTER - 2.55, PIPE_CENTER + 3.14, PIPE_CENTER - 2.55],
            [PIPE_CENTER + 2.55, PIPE_CENTER + 3.75, PIPE_CENTER + 2.55],
            omit_faces={"down"},
        ),
        box(
            "intersection-bottom-face",
            [PIPE_CENTER - 2.55, PIPE_CENTER - 3.75, PIPE_CENTER - 2.55],
            [PIPE_CENTER + 2.55, PIPE_CENTER - 3.14, PIPE_CENTER + 2.55],
            omit_faces={"up"},
        ),
    ]


def axis_for_pair(pair: tuple[str, str]) -> str:
    if pair == ("n", "s"):
        return "z"
    if pair == ("e", "w"):
        return "x"
    return "y"


def axis_for_face(face: str) -> str:
    if face in ("n", "s"):
        return "z"
    if face in ("e", "w"):
        return "x"
    return "y"


def needs_joint(mask: str) -> bool:
    faces = set(mask)
    if len(faces) != 2:
        return True
    return not any(pair[0] in faces and pair[1] in faces for pair in OPPOSITE_PAIRS)


def all_connection_masks() -> list[str]:
    masks: list[str] = []
    for bits in range(1, 1 << len(FACE_ORDER)):
        masks.append("".join(face for index, face in enumerate(FACE_ORDER) if bits & (1 << index)))
    return masks


def canonical_shape_masks() -> list[str]:
    masks: set[str] = {"ns", "ne", "nes", "nesw", "ud"}
    horizontal_canonicals = ("n", "ns", "ne", "nes", "nesw")
    vertical_canonicals = ("u", "d", "ud")
    for vertical in vertical_canonicals:
        for horizontal in horizontal_canonicals:
            masks.add(sorted_mask([*horizontal, *vertical]))
    return [
        mask
        for mask in all_connection_masks()
        if mask in masks
    ]


def sorted_mask(faces: set[str] | tuple[str, ...] | list[str]) -> str:
    face_set = set(faces)
    return "".join(face for face in FACE_ORDER if face in face_set)


def opposite(face: str) -> str:
    return {
        "n": "s",
        "s": "n",
        "e": "w",
        "w": "e",
        "u": "d",
        "d": "u",
    }[face]


def axis_pair_for(face: str) -> str:
    return sorted_mask((face, opposite(face)))


def rotate_vector(vector: tuple[int, int, int], axis: str, degrees: int) -> tuple[int, int, int]:
    x, y, z = vector
    turns = (degrees // 90) % 4
    for _ in range(turns):
        if axis == "x":
            y, z = -z, y
        elif axis == "y":
            x, z = z, -x
        else:
            x, y = -y, x
    return x, y, z


def rotate_mask(mask: str, rotation: tuple[int, int, int]) -> str:
    rx, ry, rz = rotation
    faces: set[str] = set()
    for face in mask:
        vector = FACE_VECTORS[face]
        vector = rotate_vector(vector, "x", rx)
        vector = rotate_vector(vector, "y", ry)
        vector = rotate_vector(vector, "z", rz)
        faces.add(VECTOR_FACES[vector])
    return sorted_mask(faces)


SIMPLE_ROTATIONS = [
    (0, 0, 0),
    (0, 90, 0),
    (0, 180, 0),
    (0, 270, 0),
    (90, 0, 0),
    (270, 0, 0),
    (0, 0, 90),
    (0, 0, 270),
    (180, 0, 0),
    (0, 0, 180),
]
ROTATIONS = SIMPLE_ROTATIONS + [
    (rx, ry, rz)
    for rx in (0, 90, 180, 270)
    for ry in (0, 90, 180, 270)
    for rz in (0, 90, 180, 270)
    if (rx, ry, rz) not in SIMPLE_ROTATIONS
]


def rotation_for(canonical_mask: str, target_mask: str) -> tuple[int, int, int] | None:
    for rotation in ROTATIONS:
        if rotate_mask(canonical_mask, rotation) == target_mask:
            return rotation
    return None


def y_rotation_for(canonical_mask: str, target_mask: str) -> tuple[int, int, int] | None:
    for degrees in (0, 90, 180, 270):
        rotation = (0, degrees, 0)
        if rotate_mask(canonical_mask, rotation) == target_mask:
            return rotation
    return None


def rotation_json(rotation: tuple[int, int, int] | None) -> dict[str, int]:
    if rotation is None:
        return {}
    rx, ry, rz = rotation
    data: dict[str, int] = {}
    if rx:
        data["rotateX"] = rx
    if ry:
        data["rotateY"] = ry
    if rz:
        data["rotateZ"] = rz
    return data


def shape_entry(base: str, rotation: tuple[int, int, int] | None = None) -> dict[str, object]:
    entry: dict[str, object] = {"base": f"block/copperpipe/copperpipe-{base}"}
    entry.update(rotation_json(rotation))
    return entry


def canonical_horizontal_mask(horizontal_faces: set[str]) -> str:
    count = len(horizontal_faces)
    if count == 0:
        return ""
    if count == 1:
        return "n"
    if count == 2:
        if ("n" in horizontal_faces and "s" in horizontal_faces) or ("e" in horizontal_faces and "w" in horizontal_faces):
            return "ns"
        return "ne"
    if count == 3:
        return "nes"
    return "nesw"


def piece_for(mask: str) -> tuple[str, tuple[int, int, int] | None]:
    if len(mask) == 1:
        face = mask[0]
        if face in ("n", "s"):
            return "ns", None
        if face in ("e", "w"):
            return "ns", (0, 90, 0)
        return "ud", None

    horizontal_faces = {face for face in mask if face in HORIZONTAL_FACE_ORDER}
    vertical_faces = {face for face in mask if face in ("u", "d")}
    canonical_mask = sorted_mask([*canonical_horizontal_mask(horizontal_faces), *vertical_faces])
    rotation = y_rotation_for(canonical_mask, mask)
    if rotation is None:
        raise ValueError(f"No Y rotation maps {canonical_mask} to {mask}")
    return canonical_mask, rotation


def shape_for(mask: str) -> dict[str, object]:
    if mask == "ne":
        return shape_document(
            textures=TEXTURES,
            elements=elbow_ne_elements(),
            texture_width=16,
            texture_height=16,
        )

    faces = set(mask)
    elements: list[dict[str, object]] = []
    consumed: set[str] = set()

    for pair in OPPOSITE_PAIRS:
        if pair[0] in faces and pair[1] in faces:
            start, end = axial_bounds_for_pair(pair)
            elements.extend(tube_elements(axis_for_pair(pair), start, end, f"pipe-{pair[0]}{pair[1]}"))
            consumed.update(pair)

    arm_index = 0
    for face in FACE_ORDER:
        if face not in faces or face in consumed:
            continue
        start, end = axial_bounds_for_face(face, offset_for(arm_index))
        elements.extend(tube_elements(axis_for_face(face), start, end, f"pipe-{face}"))
        arm_index += 1

    if needs_joint(mask):
        elements.extend(intersection_fitting_elements())

    return shape_document(
        textures=TEXTURES,
        elements=elements,
        texture_width=16,
        texture_height=16,
    )


def pump_cog_elements() -> list[dict[str, object]]:
    shape = json.loads(COGWHEEL_SHAPE_IN.read_text(encoding="utf-8"))
    elements: list[dict[str, object]] = []
    for element in shape.get("elements", []):
        name = str(element.get("name", ""))
        if not (name.startswith("hub_outer_") or name.startswith("tooth_")):
            continue

        pump_element = copy.deepcopy(element)
        pump_element["name"] = f"pump-cog-{name}"
        elements.append(pump_element)

    return elements


def pump_cog_rotator_names() -> list[str]:
    names = [f"pump-cog-hub_outer_{angle:02d}" for angle in (0, 36, 72, 108, 144)]
    names.extend(f"pump-cog-tooth_{angle:02d}" for angle in (0, 36, 72, 108, 144, 180, 216, 252, 288, 324))
    return names


def pump_arrow_elements() -> list[dict[str, object]]:
    return [
        textured(
            box("pump-arrow-shaft", [7.25, 14.05, 2.25], [8.75, 14.65, 10.75], omit_faces={"down"}),
            "#dark",
        ),
        textured(
            box("pump-arrow-head", [5.65, 14.04, 10.25], [10.35, 14.66, 12.35], omit_faces={"down"}),
            "#dark",
        ),
        textured(
            rotated_box(
                "pump-arrow-head-left",
                [5.75, 14.03, 9.2],
                [7.45, 14.67, 12.25],
                rotation_origin=[8.0, 14.35, 10.75],
                rotation_axis="y",
                rotation_degrees=35,
                omit_faces={"down"},
            ),
            "#dark",
        ),
        textured(
            rotated_box(
                "pump-arrow-head-right",
                [8.55, 14.02, 9.2],
                [10.25, 14.68, 12.25],
                rotation_origin=[8.0, 14.35, 10.75],
                rotation_axis="y",
                rotation_degrees=-35,
                omit_faces={"down"},
            ),
            "#dark",
        ),
    ]


def pump_shape() -> dict[str, object]:
    elements: list[dict[str, object]] = []
    elements.extend(pump_cog_elements())
    elements.extend(tube_elements("z", 0.0, 16.0, "pump-pipe"))
    elements.extend(solid_round_elements("z", 6.65, 9.35, "pump-center-sleeve", OUTER_RADIUS + 0.35))
    elements.extend(pump_arrow_elements())
    return shape_document(
        textures=TEXTURES,
        elements=elements,
        texture_width=16,
        texture_height=16,
    )


def collision_arm(face: str) -> dict[str, float]:
    lo = (PIPE_CENTER - OUTER_RADIUS) / 16.0
    hi = (PIPE_CENTER + OUTER_RADIUS) / 16.0
    center = PIPE_CENTER / 16.0
    if face == "n":
        return {"x1": lo, "y1": lo, "z1": 0.0, "x2": hi, "y2": hi, "z2": center}
    if face == "s":
        return {"x1": lo, "y1": lo, "z1": center, "x2": hi, "y2": hi, "z2": 1.0}
    if face == "e":
        return {"x1": center, "y1": lo, "z1": lo, "x2": 1.0, "y2": hi, "z2": hi}
    if face == "w":
        return {"x1": 0.0, "y1": lo, "z1": lo, "x2": center, "y2": hi, "z2": hi}
    if face == "u":
        return {"x1": lo, "y1": center, "z1": lo, "x2": hi, "y2": 1.0, "z2": hi}
    return {"x1": lo, "y1": 0.0, "z1": lo, "x2": hi, "y2": center, "z2": hi}


def collision_boxes(mask: str) -> list[dict[str, float]]:
    return [collision_arm(face) for face in FACE_ORDER if face in effective_collision_mask(mask)]


def effective_collision_mask(mask: str) -> str:
    return axis_pair_for(mask) if len(mask) == 1 else mask


def is_straight_mask(mask: str) -> bool:
    faces = set(mask)
    if len(faces) == 1:
        return True
    if len(faces) != 2:
        return False
    return any(pair[0] in faces and pair[1] in faces for pair in OPPOSITE_PAIRS)


def merged_box(boxes: list[dict[str, float]]) -> dict[str, float]:
    return {
        "x1": min(box["x1"] for box in boxes),
        "y1": min(box["y1"] for box in boxes),
        "z1": min(box["z1"] for box in boxes),
        "x2": max(box["x2"] for box in boxes),
        "y2": max(box["y2"] for box in boxes),
        "z2": max(box["z2"] for box in boxes),
    }


def straight_collision_box_by_type(masks: list[str]) -> dict[str, dict[str, float]]:
    return {
        f"*-{mask}": merged_box(collision_boxes(mask))
        for mask in masks
        if is_straight_mask(mask)
    }


def blocktype() -> dict[str, object]:
    masks = all_connection_masks()
    shape_by_type = {
        f"*-{mask}": shape_entry(*piece_for(mask))
        for mask in masks
    }
    boxes_by_type = {
        f"*-{mask}": collision_boxes(mask)
        for mask in masks
    }
    return {
        "code": "copperpipe",
        "class": "BlockCopperPipe",
        "entityClass": "CopperPipe",
        "variantgroups": [
            {"code": "conn", "states": masks},
        ],
        "shape": {"base": "block/copperpipe/copperpipe-ns"},
        "shapeByType": shape_by_type,
        "attributes": {
            "pipe": True,
            "connectsLiquidPipe": True,
        },
        "blockmaterial": "Metal",
        "resistance": 1.5,
        "requiredMiningTier": 1,
        "creativeinventory": {
            "general": ["*-ns"],
            "vintagekinematics": ["*-ns"],
        },
        "maxstacksize": 64,
        "lightAbsorption": 0,
        "sideopaque": {"all": "false"},
        "sidesolid": {"all": "false"},
        "collisionselectionboxByType": straight_collision_box_by_type(masks),
        "collisionboxesbytype": boxes_by_type,
        "selectionboxesbytype": boxes_by_type,
        "sounds": {
            "place": "game:block/anvil",
            "break": "game:block/anvil",
            "hit": "game:block/anvil",
        },
    }


def pump_shape_entry(direction: str) -> dict[str, object]:
    rotations = {
        "n": (0, 180, 0),
        "e": (0, 90, 0),
        "s": None,
        "w": (0, 270, 0),
        "u": (270, 0, 0),
        "d": (90, 0, 0),
    }
    entry: dict[str, object] = {"base": "block/copperpipe/copperpump"}
    entry.update(rotation_json(rotations[direction]))
    return entry


def pump_kinetic_axis(direction: str) -> str:
    if direction in ("e", "w"):
        return "X"
    if direction in ("u", "d"):
        return "Y"
    return "Z"


def pump_entity_behaviors(direction: str) -> list[dict[str, object]]:
    axis = pump_kinetic_axis(direction)
    return [
        {
            "name": "Kinetic",
            "properties": {
                "role": "SmallCogwheel",
                "stressImpact": 16,
                "axis": axis,
            },
        },
        {
            "name": "KineticAnimator",
            "properties": {
                "rotators": [
                    {"element": name, "axis": "Z", "ratio": 1}
                    for name in pump_cog_rotator_names()
                ],
            },
        },
    ]


def pump_blocktype() -> dict[str, object]:
    shape_by_type = {
        f"*-{face}": pump_shape_entry(face)
        for face in FACE_ORDER
    }
    boxes_by_type = {
        f"*-{face}": collision_boxes(axis_pair_for(face))
        for face in FACE_ORDER
    }
    return {
        "code": "copperpump",
        "class": "BlockCopperPump",
        "entityClass": "CopperPump",
        "entityBehaviorsByType": {
            f"*-{face}": pump_entity_behaviors(face)
            for face in FACE_ORDER
        },
        "variantgroups": [
            {"code": "direction", "states": list(FACE_ORDER)},
        ],
        "shape": {"base": "block/copperpipe/copperpump"},
        "shapeByType": shape_by_type,
        "attributes": {
            "pipe": True,
            "connectsLiquidPipe": True,
            "liquidPump": True,
        },
        "blockmaterial": "Metal",
        "resistance": 1.5,
        "requiredMiningTier": 1,
        "creativeinventory": {
            "general": ["*-s"],
            "vintagekinematics": ["*-s"],
        },
        "maxstacksize": 64,
        "lightAbsorption": 0,
        "sideopaque": {"all": "false"},
        "sidesolid": {"all": "false"},
        "collisionboxesbytype": boxes_by_type,
        "selectionboxesbytype": boxes_by_type,
        "sounds": {
            "place": "game:block/anvil",
            "break": "game:block/anvil",
            "hit": "game:block/anvil",
        },
    }


def main() -> None:
    SHAPE_DIR.mkdir(parents=True, exist_ok=True)
    masks = canonical_shape_masks()
    for mask in masks:
        write_json(SHAPE_DIR / f"copperpipe-{mask}.json", shape_for(mask))
    write_json(SHAPE_DIR / "copperpump.json", pump_shape())
    write_json(BLOCKTYPE_OUT, blocktype())
    write_json(PUMP_BLOCKTYPE_OUT, pump_blocktype())
    print(f"Generated {len(masks)} copper pipe shape(s) and 1 pump shape")
    print(BLOCKTYPE_OUT)
    print(PUMP_BLOCKTYPE_OUT)


if __name__ == "__main__":
    main()
