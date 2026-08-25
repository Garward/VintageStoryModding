"""Dedicated north-facing belt and kinetic storage port faces."""

from __future__ import annotations

import math

from shapegen import Element, ShapeBuilder

from .casing import framed_surfaces
from .materials import TEXTURE_SIZE
from .surfaces import face_patch


def _port_backplate(name: str, *, thick_front: bool = False) -> list[Element]:
    elements = [
        element
        for element in framed_surfaces()
        if not element["name"].startswith("north-")
    ]
    if thick_front:
        builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
        elements.append(
            builder.box(
                f"{name}-front-bulkhead",
                [1.3, 1.3, 1.0],
                [14.7, 14.7, 1.59],
                "#panel",
                append=False,
            )
        )
    else:
        elements.append(
            face_patch(
                f"{name}-front",
                "north",
                1.25,
                1.25,
                14.75,
                14.75,
                "#panel",
                depth=0.35,
                inset=1.25,
            )
        )
    surface = _north_patch_with_returns if name.startswith("belt-") else face_patch
    elements.extend(
        [
            surface(f"{name}-plate", "north", 2.0, 2.0, 14.0, 14.0, "#trim", depth=0.45, inset=0.65),
            surface(f"{name}-recess", "north", 3.25, 3.25, 12.75, 12.75, "#recess", depth=0.5, inset=0.15),
        ]
    )
    return elements


def _north_patch_with_returns(
    name: str,
    face: str,
    u1: float,
    v1: float,
    u2: float,
    v2: float,
    texture: str,
    *,
    depth: float,
    inset: float,
) -> Element:
    """North-facing patch with four visible depth returns and no buried rear cap."""

    if face != "north":
        raise ValueError("Capped storage-port patches must face north")

    patch = face_patch(name, face, u1, v1, u2, v2, texture, depth=depth, inset=inset)
    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    cuboid = builder.box(
        name,
        [u1, v1, inset],
        [u2, v2, inset + depth],
        texture,
        append=False,
    )
    for return_face in ("west", "east", "down", "up"):
        patch["faces"][return_face] = cuboid["faces"][return_face]
    return patch


def belt_port(*, output: bool) -> list[Element]:
    name = "belt-output" if output else "belt-input"
    mouth_left = 4.0 if output else 2.25
    mouth_right = 12.0 if output else 13.75
    mouth_bottom = 11.05 if output else 8.25
    mouth_top = 13.25
    elements = _port_backplate(name, thick_front=not output)
    elements.extend(
        [
            # The input aperture surrounds the belt's 11/16-block item deck so items
            # visibly travel into the machine instead of meeting a low wall socket.
            # These are outward-only faces; the belt approach volume remains empty.
            face_patch(
                f"{name}-mouth",
                "north",
                mouth_left,
                mouth_bottom,
                mouth_right,
                mouth_top,
                "#recess",
                depth=0.47,
                inset=0.08,
            ),
        ]
    )
    if not output:
        elements.extend(_belt_throat(name))
        elements.extend(_belt_direction_chevrons(name, output=False))
        elements.extend(_belt_input_hood())
    else:
        elements.extend(_belt_output_chute())
    return elements


def _belt_throat(name: str) -> list[Element]:
    """Solid U-shaped mouth joining the face assembly to the hood plane."""

    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    bottom = builder.box(
        f"{name}-throat-bottom",
        [1.25, 7.5, 0.0],
        [14.75, 8.25, 0.99],
        "#trim",
        append=False,
    )
    top = builder.box(
        f"{name}-throat-top",
        [1.25, 13.25, 0.0],
        [14.75, 14.5, 0.99],
        "#trim",
        append=False,
    )
    left = builder.box(
        f"{name}-throat-left",
        [1.25, 8.25, 0.0],
        [2.25, 13.25, 0.99],
        "#trim",
        append=False,
    )
    right = builder.box(
        f"{name}-throat-right",
        [13.75, 8.25, 0.0],
        [14.75, 13.25, 0.99],
        "#trim",
        append=False,
    )
    for jamb in (left, right):
        jamb["faces"].pop("down", None)
        jamb["faces"].pop("up", None)
    return [bottom, top, left, right]


def _belt_direction_chevrons(name: str, *, output: bool) -> list[Element]:
    """Long paired chevrons: converging for input, diverging for output."""

    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    elements: list[Element] = []
    for symbol, tip_x, points_right in (
        ("left", 4.5 if output else 7.0, not output),
        ("right", 11.5 if output else 9.0, output),
    ):
        elements.extend(
            _chevron_bars(
                builder,
                f"{name}-direction-{symbol}",
                tip_x,
                points_right=points_right,
            )
        )
    return elements


def _chevron_bars(
    builder: ShapeBuilder,
    name: str,
    tip_x: float,
    *,
    points_right: bool,
) -> list[Element]:
    """Create two substantial angled bars whose inner corners meet without layering."""

    length = 3.0
    thickness = 0.7
    half_length = length / 2
    half_thickness = thickness / 2
    endpoint_sign = 1 if points_right else -1
    upper_angle = -30 if points_right else 30
    lower_angle = -upper_angle
    tip_separation = half_thickness * math.cos(math.radians(30))
    bars: list[Element] = []
    for part, angle, tip_y in (
        ("upper", upper_angle, 10.5 + tip_separation),
        ("lower", lower_angle, 10.5 - tip_separation),
    ):
        radians = math.radians(angle)
        center_x = tip_x - endpoint_sign * half_length * math.cos(radians)
        center_y = tip_y - endpoint_sign * half_length * math.sin(radians)
        bar = builder.box(
            f"{name}-{part}",
            [center_x - half_length, center_y - half_thickness, 0.0],
            [center_x + half_length, center_y + half_thickness, 0.35],
            "#route",
            rotation_origin=[center_x, center_y, 0.175],
            rotation_z=angle,
            append=False,
        )
        bar["faces"] = {"north": bar["faces"]["north"]}
        bars.append(bar)
    return bars


def _belt_input_hood() -> list[Element]:
    """Open-bottom canopy projecting over the adjacent belt and item path."""

    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    pieces = [
        builder.box("belt-input-hood-left", [1.25, 11.05, -3.5], [2.25, 13.25, 0.0], "#trim", append=False),
        builder.box("belt-input-hood-top", [1.25, 13.25, -3.5], [14.75, 14.5, 0.0], "#trim", append=False),
        builder.box("belt-input-hood-right", [13.75, 11.05, -3.5], [14.75, 13.25, 0.0], "#trim", append=False),
    ]
    pieces[0]["faces"].pop("up", None)
    pieces[2]["faces"].pop("up", None)
    for piece in pieces:
        # The hood terminates at the port face; its hidden attachment cap would
        # otherwise sit on top of the face lips.
        piece["faces"].pop("south", None)
    return pieces


def _belt_output_chute() -> list[Element]:
    """Stepped open-bottom nozzle projecting over the destination belt."""

    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    rear = _open_bottom_chute_stage(
        builder,
        "rear",
        outer_left=3.0,
        inner_left=4.0,
        inner_right=12.0,
        outer_right=13.0,
        inner_top=13.25,
        outer_top=14.25,
        z1=-1.0,
        z2=0.0,
    )
    shoulder = _open_bottom_chute_stage(
        builder,
        "shoulder",
        outer_left=3.0,
        inner_left=5.25,
        inner_right=10.75,
        outer_right=13.0,
        inner_top=12.75,
        outer_top=14.25,
        z1=-1.75,
        z2=-1.0,
    )
    front = _open_bottom_chute_stage(
        builder,
        "front",
        outer_left=4.25,
        inner_left=5.25,
        inner_right=10.75,
        outer_right=11.75,
        inner_top=12.75,
        outer_top=13.75,
        z1=-3.0,
        z2=-1.75,
    )

    for rear_piece in rear:
        rear_piece["faces"].pop("south", None)
        rear_piece["faces"].pop("north", None)
    for front_piece in front:
        front_piece["faces"].pop("south", None)

    pieces = rear + shoulder + front

    pieces.extend(_belt_output_top_arrow(builder))
    return pieces


def _open_bottom_chute_stage(
    builder: ShapeBuilder,
    label: str,
    *,
    outer_left: float,
    inner_left: float,
    inner_right: float,
    outer_right: float,
    inner_top: float,
    outer_top: float,
    z1: float,
    z2: float,
) -> list[Element]:
    """Build one solid U-shaped stage without overlapping its internal corners."""

    left = builder.box(
        f"belt-output-chute-{label}-left",
        [outer_left, 11.05, z1],
        [inner_left, inner_top, z2],
        "#trim",
        append=False,
    )
    top = builder.box(
        f"belt-output-chute-{label}-top",
        [outer_left, inner_top, z1],
        [outer_right, outer_top, z2],
        "#trim",
        append=False,
    )
    right = builder.box(
        f"belt-output-chute-{label}-right",
        [inner_right, 11.05, z1],
        [outer_right, inner_top, z2],
        "#trim",
        append=False,
    )
    left["faces"].pop("up", None)
    right["faces"].pop("up", None)
    return [left, top, right]


def _belt_output_top_arrow(builder: ShapeBuilder) -> list[Element]:
    """Blocky copper arrow pointing outward along the top of the output nozzle."""

    pieces = [
        builder.box(
            "belt-output-arrow-tip",
            [7.5, 13.76, -2.85],
            [8.5, 13.86, -2.35],
            "#route",
            append=False,
        ),
        builder.box(
            "belt-output-arrow-wings",
            [6.5, 13.76, -2.35],
            [9.5, 13.86, -1.85],
            "#route",
            append=False,
        ),
        builder.box(
            "belt-output-arrow-shaft",
            [7.5, 13.76, -1.85],
            [8.5, 13.86, -1.55],
            "#route",
            append=False,
        ),
    ]
    for piece in pieces:
        piece["faces"] = {"up": piece["faces"]["up"]}
    return pieces


def kinetic_port() -> list[Element]:
    elements = _port_backplate("kinetic-input")
    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    # Sawmill-compatible 3.3-wide centered shaft stub, fully inside the block.
    bottom = builder.box("kinetic-input-collar-bottom", [4.75, 4.75, 0.0], [11.25, 6.35, 1.1], "#trim")
    top = builder.box("kinetic-input-collar-top", [4.75, 9.65, 0.0], [11.25, 11.25, 1.1], "#trim")
    left = builder.box("kinetic-input-collar-left", [4.75, 6.35, 0.0], [6.35, 9.65, 1.1], "#trim")
    right = builder.box("kinetic-input-collar-right", [9.65, 6.35, 0.0], [11.25, 9.65, 1.1], "#trim")
    shaft = builder.box("kinetic-input-shaft", [6.35, 6.35, 0.0], [9.65, 9.65, 0.75], "#shaft")
    for collar_side in (left, right):
        collar_side["faces"].pop("down", None)
        collar_side["faces"].pop("up", None)
    for buried_face in ("down", "up", "west", "east"):
        shaft["faces"].pop(buried_face, None)
    elements.extend([bottom, top, left, right, shaft])
    return elements
