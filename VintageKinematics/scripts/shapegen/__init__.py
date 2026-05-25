"""Small helpers for generating VintageKinematics shape JSON."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Sequence


Number = float | int
Vec3 = Sequence[Number]
Element = dict[str, Any]

SIDES = ("north", "east", "south", "west", "up", "down")
Y_ROTATION_FACE_ORDER = ("north", "west", "south", "east")
DEFAULT_ROTATION_CENTER: tuple[Number, Number, Number] = (8, 8, 8)


def num(value: Number) -> Number:
    rounded = round(float(value), 4)
    if rounded.is_integer():
        return int(rounded)
    return rounded


def coords(values: list[Number]) -> list[Number]:
    return [num(value) for value in values]


def clamped_uv(values: list[Number], texture_size: int = 64) -> list[Number]:
    return [num(max(0, min(texture_size, value))) for value in values]


def simple_faces(texture: str, uv: list[Number] | None = None) -> dict[str, dict[str, Any]]:
    face_uv = coords(uv or [0, 0, 16, 16])
    return {side: {"texture": texture, "uv": face_uv} for side in SIDES}


def rotate_y_point(point: Vec3, turns: int, *, center: Vec3 = DEFAULT_ROTATION_CENTER) -> Vec3:
    """Rotate a point around Y in Vintage Story rotateY quarter-turns."""

    normalized = turns % 4
    x = float(point[0])
    y = float(point[1])
    z = float(point[2])
    cx = float(center[0])
    cz = float(center[2])

    for _ in range(normalized):
        x, z = cx + (z - cz), cz - (x - cx)

    return coords([x, y, z])


def rotate_y_box(from_: Vec3, to: Vec3, turns: int, *, center: Vec3 = DEFAULT_ROTATION_CENTER) -> tuple[Vec3, Vec3]:
    corners = [
        rotate_y_point([x, from_[1], z], turns, center=center)
        for x in (from_[0], to[0])
        for z in (from_[2], to[2])
    ]
    xs = [corner[0] for corner in corners]
    zs = [corner[2] for corner in corners]
    ys = [from_[1], to[1]]

    return coords([min(xs), min(ys), min(zs)]), coords([max(xs), max(ys), max(zs)])


def rotate_y_face_name(face: str, turns: int) -> str:
    normalized = turns % 4
    if face not in Y_ROTATION_FACE_ORDER:
        return face

    old_index = Y_ROTATION_FACE_ORDER.index(face)
    return Y_ROTATION_FACE_ORDER[(old_index + normalized) % 4]


def rotate_y_faces(faces: dict[str, Any], turns: int) -> dict[str, Any]:
    return {rotate_y_face_name(face, turns): face_data for face, face_data in faces.items()}


def rotate_y_rotation_fields(element: Element, turns: int) -> None:
    normalized = turns % 4
    if normalized == 0:
        return

    transforms: dict[int, dict[str, tuple[str, int]]] = {
        1: {
            "rotationX": ("rotationZ", -1),
            "rotationY": ("rotationY", 1),
            "rotationZ": ("rotationX", 1),
        },
        2: {
            "rotationX": ("rotationX", -1),
            "rotationY": ("rotationY", 1),
            "rotationZ": ("rotationZ", -1),
        },
        3: {
            "rotationX": ("rotationZ", 1),
            "rotationY": ("rotationY", 1),
            "rotationZ": ("rotationX", -1),
        },
    }

    rotated: dict[str, Number] = {}
    for old_key, (new_key, sign) in transforms[normalized].items():
        if old_key in element:
            rotated[new_key] = num(float(rotated.get(new_key, 0)) + (float(element.pop(old_key)) * sign))

    for key, value in rotated.items():
        element[key] = value


def renamed_with_replacements(name: str, replacements: list[tuple[str, str]] | tuple[tuple[str, str], ...]) -> str:
    renamed = name
    for old, new in replacements:
        renamed = renamed.replace(old, new)
    return renamed


def rotate_y_element(
    element: Element,
    turns: int,
    *,
    center: Vec3 = DEFAULT_ROTATION_CENTER,
    name_replacements: list[tuple[str, str]] | tuple[tuple[str, str], ...] = (),
    rotate_children: bool = False,
) -> Element:
    if "name" in element and name_replacements:
        element["name"] = renamed_with_replacements(str(element["name"]), name_replacements)

    if "from" in element and "to" in element:
        element["from"], element["to"] = rotate_y_box(element["from"], element["to"], turns, center=center)

    for key in ("rotationOrigin", "origin"):
        if key in element:
            element[key] = rotate_y_point(element[key], turns, center=center)

    if "faces" in element:
        element["faces"] = rotate_y_faces(element["faces"], turns)

    rotate_y_rotation_fields(element, turns)

    if rotate_children and "children" in element:
        element["children"] = rotate_y_elements(
            element["children"],
            turns,
            center=center,
            name_replacements=name_replacements,
            rotate_children=True,
        )

    return element


def rotate_y_elements(
    elements: list[Element],
    turns: int,
    *,
    center: Vec3 = DEFAULT_ROTATION_CENTER,
    name_replacements: list[tuple[str, str]] | tuple[tuple[str, str], ...] = (),
    rotate_children: bool = False,
) -> list[Element]:
    return [
        rotate_y_element(
            element,
            turns,
            center=center,
            name_replacements=name_replacements,
            rotate_children=rotate_children,
        )
        for element in elements
    ]


class ShapeBuilder:
    """Cuboid shape builder with optional controller-relative coordinate output."""

    def __init__(self, *, texture_size: int = 64, offset: Vec3 | None = None) -> None:
        self.texture_size = texture_size
        self.offset = offset or [0, 0, 0]
        self.elements: list[Element] = []

    @classmethod
    def controller_relative(cls, cposition: Vec3, *, texture_size: int = 64) -> "ShapeBuilder":
        return cls(
            texture_size=texture_size,
            offset=[float(value) * 16 for value in cposition],
        )

    def transformed(self, values: Vec3) -> Vec3:
        return coords([float(value) - float(self.offset[index]) for index, value in enumerate(values)])

    def cuboid_faces(
        self,
        texture: str,
        from_: Vec3,
        to: Vec3,
        *,
        top: str | None = None,
        bottom: str | None = None,
        north: str | None = None,
        south: str | None = None,
        east: str | None = None,
        west: str | None = None,
    ) -> dict[str, dict[str, Any]]:
        x = abs(float(to[0]) - float(from_[0]))
        y = abs(float(to[1]) - float(from_[1]))
        z = abs(float(to[2]) - float(from_[2]))
        return {
            "north": {"texture": north or texture, "uv": clamped_uv([0, 0, x, y], self.texture_size)},
            "east": {"texture": east or texture, "uv": clamped_uv([0, 0, z, y], self.texture_size)},
            "south": {"texture": south or texture, "uv": clamped_uv([0, 0, x, y], self.texture_size)},
            "west": {"texture": west or texture, "uv": clamped_uv([0, 0, z, y], self.texture_size)},
            "up": {"texture": top or texture, "uv": clamped_uv([0, 0, x, z], self.texture_size)},
            "down": {"texture": bottom or texture, "uv": clamped_uv([0, 0, x, z], self.texture_size)},
        }

    def box(
        self,
        name: str,
        from_: Vec3,
        to: Vec3,
        texture: str,
        *,
        top: str | None = None,
        bottom: str | None = None,
        north: str | None = None,
        south: str | None = None,
        east: str | None = None,
        west: str | None = None,
        rotation_origin: Vec3 | None = None,
        rotation_x: Number | None = None,
        rotation_y: Number | None = None,
        rotation_z: Number | None = None,
        children: list[Element] | None = None,
        append: bool = True,
    ) -> Element:
        element: Element = {
            "name": name,
            "from": self.transformed(from_),
            "to": self.transformed(to),
            "faces": self.cuboid_faces(texture, from_, to, top=top, bottom=bottom, north=north, south=south, east=east, west=west),
        }
        if rotation_origin is not None:
            element["rotationOrigin"] = self.transformed(rotation_origin)
        if rotation_x is not None:
            element["rotationX"] = num(rotation_x)
        if rotation_y is not None:
            element["rotationY"] = num(rotation_y)
        if rotation_z is not None:
            element["rotationZ"] = num(rotation_z)
        if children is not None:
            element["children"] = children
        if append:
            self.elements.append(element)
        return element

    def extend(self, elements: list[Element]) -> None:
        self.elements.extend(elements)


def shape_document(
    *,
    textures: dict[str, str],
    elements: list[Element],
    texture_width: int = 64,
    texture_height: int = 64,
    editor: bool = True,
) -> dict[str, Any]:
    shape: dict[str, Any] = {
        "textureWidth": texture_width,
        "textureHeight": texture_height,
        "textureSizes": {},
        "textures": textures,
        "elements": elements,
    }
    if editor:
        shape = {
            "editor": {
                "allAngles": False,
                "entityTextureMode": False,
            },
            **shape,
        }
    return shape


def write_json(path: Path, data: dict[str, Any]) -> None:
    path.write_text(json.dumps(data, indent=4) + "\n", encoding="utf-8")
