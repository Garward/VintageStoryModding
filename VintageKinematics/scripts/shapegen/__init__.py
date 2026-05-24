"""Small helpers for generating VintageKinematics shape JSON."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


Number = float | int
Vec3 = list[Number]
Element = dict[str, Any]

SIDES = ("north", "east", "south", "west", "up", "down")


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
