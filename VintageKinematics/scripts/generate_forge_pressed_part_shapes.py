#!/usr/bin/env python3
"""Regenerate detailed forge-pressed item part shapes."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
SHAPE_ROOT = ROOT / "assets/vintagekinematics/shapes"
METAL_TEXTURE = "game:block/metal/ingot/iron"
ORIGIN = [8, 8, 8]
COPLANAR_FACE_OFFSET = 0.01
REPEATED_SEGMENT_OFFSET = COPLANAR_FACE_OFFSET * 2


Element = dict[str, Any]


def n(value: float | int) -> float | int:
    rounded = round(float(value), 2)
    if rounded.is_integer():
        return int(rounded)
    return rounded


def coords(values: list[float | int]) -> list[float | int]:
    return [n(value) for value in values]


def faces(texture: str, from_: list[float | int], to: list[float | int]) -> dict[str, dict[str, Any]]:
    x = abs(float(to[0]) - float(from_[0]))
    y = abs(float(to[1]) - float(from_[1]))
    z = abs(float(to[2]) - float(from_[2]))
    return {
        "north": {"texture": texture, "uv": coords([0, 0, x, y])},
        "east": {"texture": texture, "uv": coords([0, 0, z, y])},
        "south": {"texture": texture, "uv": coords([0, 0, x, y])},
        "west": {"texture": texture, "uv": coords([0, 0, z, y])},
        "up": {"texture": texture, "uv": coords([0, 0, x, z])},
        "down": {"texture": texture, "uv": coords([0, 0, x, z])},
    }


def box(
    name: str,
    from_: list[float | int],
    to: list[float | int],
    texture: str = "#metal",
    *,
    rotation_origin: list[float | int] | None = None,
    rotation_x: float | None = None,
    rotation_y: float | None = None,
    rotation_z: float | None = None,
) -> Element:
    element: Element = {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": faces(texture, from_, to),
    }
    if rotation_origin is not None:
        element["rotationOrigin"] = coords(rotation_origin)
    if rotation_x is not None:
        element["rotationX"] = n(rotation_x)
    if rotation_y is not None:
        element["rotationY"] = n(rotation_y)
    if rotation_z is not None:
        element["rotationZ"] = n(rotation_z)
    return element


def y_ring(
    name: str,
    y1: float,
    y2: float,
    inner_radius: float,
    outer_radius: float,
    tangent_width: float,
    *,
    count: int = 16,
    stagger_y_step: float = REPEATED_SEGMENT_OFFSET,
) -> list[Element]:
    elements: list[Element] = []
    half_width = tangent_width / 2
    for index in range(count):
        angle = index * 360 / count
        y_offset = index * stagger_y_step
        elements.append(
            box(
                f"{name}-{index:02d}",
                [8 - half_width, y1 + y_offset, 8 + inner_radius],
                [8 + half_width, y2 + y_offset, 8 + outer_radius],
                rotation_origin=ORIGIN if angle else None,
                rotation_y=angle if angle else None,
            )
        )
    return elements


def x_cylinder_shell(
    name: str,
    x1: float,
    x2: float,
    inner_radius: float,
    outer_radius: float,
    tangent_width: float,
    *,
    count: int = 16,
    stagger_x_step: float = REPEATED_SEGMENT_OFFSET,
) -> list[Element]:
    elements: list[Element] = []
    half_width = tangent_width / 2
    for index in range(count):
        angle = index * 360 / count
        x_offset = index * stagger_x_step
        elements.append(
            box(
                f"{name}-{index:02d}",
                [x1 + x_offset, 8 - half_width, 8 + inner_radius],
                [x2 + x_offset, 8 + half_width, 8 + outer_radius],
                rotation_origin=ORIGIN if angle else None,
                rotation_x=angle if angle else None,
            )
        )
    return elements


def radial_boxes(
    name: str,
    from_: list[float | int],
    to: list[float | int],
    *,
    count: int,
    step: float,
    rotation_axis: str = "y",
    start: float = 0,
    stagger_y_step: float = REPEATED_SEGMENT_OFFSET,
) -> list[Element]:
    elements: list[Element] = []
    for index in range(count):
        angle = start + index * step
        y_offset = index * stagger_y_step if rotation_axis == "y" else 0
        offset_from = list(from_)
        offset_to = list(to)
        offset_from[1] = float(offset_from[1]) + y_offset
        offset_to[1] = float(offset_to[1]) + y_offset
        kwargs: dict[str, Any] = {}
        if angle:
            kwargs["rotation_origin"] = ORIGIN
        if rotation_axis == "x":
            kwargs["rotation_x"] = angle if angle else None
        elif rotation_axis == "z":
            kwargs["rotation_z"] = angle if angle else None
        else:
            kwargs["rotation_y"] = angle if angle else None
        elements.append(box(f"{name}-{index:02d}", offset_from, offset_to, **kwargs))
    return elements


def document(elements: list[Element], textures: dict[str, str] | None = None) -> dict[str, Any]:
    return {
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": textures or {"metal": METAL_TEXTURE},
        "elements": elements,
    }


def transform_signature(element: Element) -> tuple[Any, ...]:
    return (
        tuple(element.get("rotationOrigin", ())),
        element.get("rotationX"),
        element.get("rotationY"),
        element.get("rotationZ"),
    )


def spans_overlap(a: tuple[float, float], b: tuple[float, float]) -> bool:
    return max(a[0], b[0]) < min(a[1], b[1]) - 1e-9


def adjust_face(element: Element, axis: int, side: str) -> None:
    key = "from" if side == "min" else "to"
    direction = 1 if side == "min" else -1
    values = list(element[key])
    values[axis] = n(float(values[axis]) + (direction * COPLANAR_FACE_OFFSET))
    element[key] = values


def avoid_coplanar_faces(elements: list[Element]) -> list[Element]:
    axis_pairs = ((0, (1, 2)), (1, (0, 2)), (2, (0, 1)))
    max_passes = 512

    for _ in range(max_passes):
        changed = False
        for i, first in enumerate(elements):
            first_sig = transform_signature(first)
            for second in elements[i + 1 :]:
                if transform_signature(second) != first_sig:
                    continue

                for axis, projection_axes in axis_pairs:
                    faces = (
                        ("min", float(first["from"][axis])),
                        ("max", float(first["to"][axis])),
                    )
                    other_faces = (
                        ("min", float(second["from"][axis])),
                        ("max", float(second["to"][axis])),
                    )

                    for _, first_coord in faces:
                        for second_side, second_coord in other_faces:
                            if abs(first_coord - second_coord) > 1e-9:
                                continue
                            if not all(
                                spans_overlap(
                                    (float(first["from"][projection_axis]), float(first["to"][projection_axis])),
                                    (float(second["from"][projection_axis]), float(second["to"][projection_axis])),
                                )
                                for projection_axis in projection_axes
                            ):
                                continue

                            adjust_face(second, axis, second_side)
                            changed = True
                            break
                        if changed:
                            break
                    if changed:
                        break
                if changed:
                    break
            if changed:
                break
        if not changed:
            return elements

    return elements


def dumps_inline(data: Any) -> str:
    return json.dumps(data, separators=(", ", ": "))


def format_shape(data: dict[str, Any]) -> str:
    lines = [
        "{",
        f'\t"textureWidth": {data["textureWidth"]},',
        f'\t"textureHeight": {data["textureHeight"]},',
        f'\t"textures": {dumps_inline(data["textures"])},',
        '\t"elements": [',
    ]
    elements = data["elements"]
    for index, element in enumerate(elements):
        suffix = "," if index < len(elements) - 1 else ""
        lines.append(f"\t\t{dumps_inline(element)}{suffix}")
    lines += ["\t]", "}"]
    return "\n".join(lines) + "\n"


def write_shape(relative_path: str, elements: list[Element], textures: dict[str, str] | None = None) -> None:
    path = SHAPE_ROOT / relative_path
    avoid_coplanar_faces(elements)
    path.write_text(format_shape(document(elements, textures)), encoding="utf-8")
    print(path.relative_to(ROOT))


def pressed_band() -> list[Element]:
    elements = y_ring("thin-spring-band", 6.35, 8.05, 4.85, 6.45, 2.18)
    elements += y_ring("rolled-upper-edge", 8.05, 8.58, 5.08, 6.24, 1.72)
    elements += y_ring("rolled-lower-edge", 5.82, 6.35, 5.08, 6.24, 1.72)
    elements += [
        box("split-overlap-leaf", [5.05, 8.04, 1.14], [10.95, 9.18, 2.48]),
        box("strap-clamp-block", [6.1, 9.18, 0.82], [9.9, 10.92, 2.78]),
        box("clamp-rolled-lip", [6.55, 10.92, 1.02], [9.45, 11.56, 2.56]),
        box("rivet-left", [5.62, 9.28, 0.56], [6.72, 10.38, 1.26]),
        box("rivet-right", [9.28, 9.28, 0.56], [10.38, 10.38, 1.26]),
    ]
    return elements


def shaft_collar() -> list[Element]:
    elements = y_ring("split-collar-body", 5.05, 10.82, 3.65, 6.18, 2.12)
    elements += y_ring("top-machined-lip", 10.82, 11.45, 3.98, 5.76, 1.72)
    elements += y_ring("bottom-machined-lip", 4.42, 5.05, 3.98, 5.76, 1.72)
    elements += [
        box("clamp-ear-left", [4.58, 6.1, 1.18], [6.76, 10.96, 3.08]),
        box("clamp-ear-right", [9.24, 6.1, 1.18], [11.42, 10.96, 3.08]),
        box("clamp-gap-bridge", [6.95, 10.96, 1.36], [9.05, 12.22, 2.9]),
        box("set-screw-head", [7.16, 12.22, 1.56], [8.84, 13.48, 2.72]),
        box("split-line-marker", [7.65, 5.46, 1.03], [8.35, 10.55, 1.42]),
    ]
    return elements


def pipe_flange() -> list[Element]:
    elements = y_ring("wide-flange-face", 5.2, 7.72, 2.68, 6.54, 2.18, count=20)
    elements += y_ring("raised-neck", 7.72, 11.18, 1.88, 3.72, 1.58)
    elements += y_ring("inner-bevel-lip", 11.18, 11.86, 1.68, 3.18, 1.34)
    elements += radial_boxes(
        "bolt-pad",
        [7.1, 7.72, 1.12],
        [8.9, 9.52, 2.92],
        count=8,
        step=45,
        rotation_axis="y",
        start=22.5,
    )
    elements += radial_boxes(
        "bolt-head",
        [7.42, 9.52, 1.42],
        [8.58, 10.46, 2.58],
        count=8,
        step=45,
        rotation_axis="y",
        start=22.5,
    )
    return elements


def rough_bearing_blank() -> list[Element]:
    elements = y_ring("rough-outer-race", 4.55, 11.22, 3.04, 5.88, 2.05)
    elements += y_ring("inner-raised-race", 5.48, 12.05, 1.72, 3.16, 1.34)
    elements += y_ring("bottom-forge-flash", 3.96, 4.55, 3.42, 6.18, 1.86, count=12)
    elements += radial_boxes(
        "untrimmed-lug",
        [7.02, 11.22, 1.82],
        [8.98, 12.84, 3.26],
        count=6,
        step=60,
        rotation_axis="y",
        start=15,
    )
    elements += radial_boxes(
        "rough-file-flat",
        [7.42, 12.05, 4.7],
        [8.58, 12.86, 6.32],
        count=4,
        step=90,
        rotation_axis="y",
        start=45,
    )
    return elements


def machine_bracket() -> list[Element]:
    return [
        box("pressed-foot-plate", [1.02, 2.72, 2.06], [14.98, 4.42, 8.74]),
        box("front-foot-roll", [1.48, 4.42, 1.64], [14.52, 5.28, 3.14]),
        box("rear-upright-wall", [1.08, 3.18, 8.42], [14.92, 13.92, 10.36]),
        box("upright-top-roll", [1.64, 13.92, 8.1], [14.36, 14.74, 10.78]),
        box("left-side-flange", [1.08, 4.42, 3.02], [2.72, 11.72, 9.18]),
        box("right-side-flange", [13.28, 4.42, 3.02], [14.92, 11.72, 9.18]),
        box("center-gusset-low", [5.76, 5.02, 4.68], [10.24, 7.18, 8.86]),
        box("center-gusset-mid", [6.24, 7.18, 5.68], [9.76, 9.64, 9.42]),
        box("center-gusset-high", [6.72, 9.64, 6.72], [9.28, 12.62, 9.96]),
        box("diagonal-web-left", [3.34, 5.08, 5.5], [4.76, 12.04, 7.18], rotation_origin=[4.05, 8.56, 6.34], rotation_z=-18),
        box("diagonal-web-right", [11.24, 5.08, 5.5], [12.66, 12.04, 7.18], rotation_origin=[11.95, 8.56, 6.34], rotation_z=18),
        box("foot-boss-left", [2.18, 5.28, 3.62], [4.28, 6.62, 5.72]),
        box("foot-boss-right", [11.72, 5.28, 3.62], [13.82, 6.62, 5.72]),
        box("wall-boss-left", [2.18, 8.92, 7.68], [4.36, 10.96, 8.72]),
        box("wall-boss-right", [11.64, 8.92, 7.68], [13.82, 10.96, 8.72]),
    ]


def piston_head() -> list[Element]:
    elements = y_ring("cylindrical-skirt", 3.1, 5.06, 0.0, 4.34, 1.56)
    elements += y_ring("main-piston-wall", 5.06, 10.18, 0.0, 5.48, 1.92, count=20)
    elements += y_ring("crown-beveled-edge", 10.18, 11.08, 0.0, 4.92, 1.74)
    elements += y_ring("top-raised-pad", 11.08, 11.94, 0.0, 3.22, 1.24, count=12)
    elements += y_ring("lower-ring-groove-ridge", 4.58, 4.92, 4.76, 5.72, 1.34)
    elements += y_ring("upper-ring-groove-ridge", 8.82, 9.16, 4.86, 5.72, 1.34)
    elements += [
        box("wrist-pin-boss-left", [1.18, 5.74, 6.05], [4.18, 8.92, 9.95]),
        box("wrist-pin-boss-right", [11.82, 5.74, 6.05], [14.82, 8.92, 9.95]),
        box("pin-flat-left", [0.74, 6.48, 6.82], [1.18, 8.18, 9.18]),
        box("pin-flat-right", [14.82, 6.48, 6.82], [15.26, 8.18, 9.18]),
    ]
    return elements


def pressed_casing() -> list[Element]:
    return [
        box("pressed-floor-pan", [1.52, 2.3, 2.06], [14.48, 4.58, 14.42]),
        box("back-tall-drawn-wall", [1.76, 4.58, 2.08], [14.24, 14.38, 4.24]),
        box("left-drawn-wall", [1.52, 4.58, 4.1], [4.0, 12.66, 14.22]),
        box("right-drawn-wall", [12.0, 4.58, 4.1], [14.48, 12.66, 14.22]),
        box("front-low-lip", [3.22, 4.58, 13.68], [12.78, 7.52, 15.18]),
        box("back-rolled-rim", [2.22, 14.38, 1.76], [13.78, 15.18, 4.58]),
        box("left-rolled-rim", [1.18, 12.66, 4.56], [4.36, 13.42, 13.86]),
        box("right-rolled-rim", [11.64, 12.66, 4.56], [14.82, 13.42, 13.86]),
        box("front-rolled-rim", [3.82, 7.52, 13.34], [12.18, 8.22, 15.5]),
        box("side-mount-left", [0.82, 5.4, 7.0], [1.52, 8.68, 10.46]),
        box("side-mount-right", [14.48, 5.4, 7.0], [15.18, 8.68, 10.46]),
        box("pressed-boss-floor", [5.62, 4.58, 6.52], [10.38, 5.68, 10.8]),
    ]


def industrial_plate() -> list[Element]:
    return [
        box("octagonal-plate-core", [2.78, 4.42, 1.2], [13.22, 6.58, 14.8]),
        box("octagonal-plate-cross", [1.2, 4.42, 2.78], [14.8, 6.58, 13.22]),
        box("pressed-long-rib", [2.08, 6.58, 7.24], [13.92, 7.84, 8.76]),
        box("pressed-cross-rib", [7.24, 6.58, 2.08], [8.76, 7.84, 13.92]),
        box("diagonal-rib-a", [7.28, 7.84, 1.98], [8.72, 8.78, 14.02], rotation_origin=ORIGIN, rotation_y=45),
        box("diagonal-rib-b", [7.28, 7.84, 1.98], [8.72, 8.78, 14.02], rotation_origin=ORIGIN, rotation_y=-45),
        box("corner-boss-nw", [2.08, 6.58, 2.08], [4.06, 8.12, 4.06]),
        box("corner-boss-ne", [11.94, 6.58, 2.08], [13.92, 8.12, 4.06]),
        box("corner-boss-sw", [2.08, 6.58, 11.94], [4.06, 8.12, 13.92]),
        box("corner-boss-se", [11.94, 6.58, 11.94], [13.92, 8.12, 13.92]),
        box("center-stamp", [6.32, 8.78, 6.32], [9.68, 9.48, 9.68]),
    ]


def boiler_plate() -> list[Element]:
    elements = [
        box("curved-sheet-strip-outer-a", [1.08, 3.96, 2.32], [14.92, 5.32, 4.18], rotation_origin=ORIGIN, rotation_x=-16),
        box("curved-sheet-strip-mid-a", [1.0, 5.22, 4.08], [15.0, 6.72, 6.28], rotation_origin=ORIGIN, rotation_x=-8),
        box("curved-sheet-strip-center", [0.94, 6.28, 6.18], [15.06, 8.02, 9.82]),
        box("curved-sheet-strip-mid-b", [1.0, 5.22, 9.72], [15.0, 6.72, 11.92], rotation_origin=ORIGIN, rotation_x=8),
        box("curved-sheet-strip-outer-b", [1.08, 3.96, 11.82], [14.92, 5.32, 13.68], rotation_origin=ORIGIN, rotation_x=16),
        box("rolled-seam-spine", [1.54, 8.0, 3.76], [14.46, 9.24, 5.24]),
        box("end-flange-left", [0.66, 4.36, 3.26], [1.74, 8.68, 12.74]),
        box("end-flange-right", [14.26, 4.36, 3.26], [15.34, 8.68, 12.74]),
    ]
    for index, x in enumerate((3.12, 5.88, 8.64, 11.4)):
        elements.append(box(f"seam-rivet-{index:02d}", [x, 9.24, 3.82], [x + 1.0, 10.16, 4.82]))
    return elements


def valve_body() -> list[Element]:
    elements = x_cylinder_shell("horizontal-pipe-left", 0.72, 5.14, 0.0, 2.28, 1.18, count=12)
    elements += x_cylinder_shell("horizontal-pipe-right", 10.86, 15.28, 0.0, 2.28, 1.18, count=12)
    elements += x_cylinder_shell("rounded-valve-chamber", 3.72, 12.28, 0.0, 4.18, 1.78, count=18)
    elements += y_ring("vertical-bonnet-neck", 10.4, 14.52, 0.0, 2.28, 1.12, count=12)
    elements += y_ring("bonnet-flange", 9.44, 10.4, 1.86, 3.32, 1.18, count=12)
    elements += [
        box("hex-bonnet-cap-ns", [5.68, 14.52, 6.54], [10.32, 15.68, 9.46]),
        box("hex-bonnet-cap-ew", [6.54, 14.52, 5.68], [9.46, 15.68, 10.32]),
        box("handle-cross-x", [3.34, 15.42, 7.28], [12.66, 16.0, 8.72]),
        box("handle-cross-z", [7.28, 15.42, 3.34], [8.72, 16.0, 12.66]),
        box("pipe-lip-left", [0.38, 6.34, 6.34], [1.06, 9.66, 9.66]),
        box("pipe-lip-right", [14.94, 6.34, 6.34], [15.62, 9.66, 9.66]),
    ]
    return elements


def main() -> None:
    write_shape("item/part/pressedband.json", pressed_band())
    write_shape("item/part/shaftcollar.json", shaft_collar())
    write_shape("item/part/pipeflange.json", pipe_flange())
    write_shape("item/part/roughbearingblank.json", rough_bearing_blank())
    write_shape("item/part/machinebracket.json", machine_bracket())
    write_shape("item/part/pistonhead.json", piston_head())
    write_shape("item/part/pressedcasing.json", pressed_casing())
    write_shape("item/part/industrialplate.json", industrial_plate())
    write_shape("item/part/boilerplate.json", boiler_plate())
    write_shape("item/part/valvebody.json", valve_body())


if __name__ == "__main__":
    main()
