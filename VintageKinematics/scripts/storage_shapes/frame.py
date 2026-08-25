"""Connection-aware crate framing for modular storage members."""

from __future__ import annotations

from shapegen import Element, ShapeBuilder

from .materials import TEXTURE_SIZE


FRAME_EDGES = [
    ("edge-x-down-north", [0, 0, 0], [16, 1.25, 1.25], {"down", "north"}),
    ("edge-x-down-south", [0, 0, 14.75], [16, 1.25, 16], {"down", "south"}),
    ("edge-x-up-north", [0, 14.75, 0], [16, 16, 1.25], {"up", "north"}),
    ("edge-x-up-south", [0, 14.75, 14.75], [16, 16, 16], {"up", "south"}),
    ("edge-y-west-north", [0, 0, 0], [1.25, 16, 1.25], {"west", "north"}),
    ("edge-y-west-south", [0, 0, 14.75], [1.25, 16, 16], {"west", "south"}),
    ("edge-y-east-north", [14.75, 0, 0], [16, 16, 1.25], {"east", "north"}),
    ("edge-y-east-south", [14.75, 0, 14.75], [16, 16, 16], {"east", "south"}),
    ("edge-z-down-west", [0, 0, 0], [1.25, 1.25, 16], {"down", "west"}),
    ("edge-z-down-east", [14.75, 0, 0], [16, 1.25, 16], {"down", "east"}),
    ("edge-z-up-west", [0, 14.75, 0], [1.25, 16, 16], {"up", "west"}),
    ("edge-z-up-east", [14.75, 14.75, 0], [16, 16, 16], {"up", "east"}),
]
ELBOW_NAMES = [name.removeprefix("edge-") for name, *_ in FRAME_EDGES]


def crate_frame(connected_faces: set[str]) -> list[Element]:
    """Build an overlap-free frame around faces that remain exposed."""

    return _outer_frame(
        connected_faces,
        continuous_faces=connected_faces,
        rail_suffix="",
    )


def isolated_outer_frame(*, excluded_faces: set[str] | None = None) -> list[Element]:
    """Closed outer frame with one owner for every rail and corner volume."""

    return _outer_frame(
        excluded_faces or set(),
        continuous_faces=set(),
        rail_suffix="-item",
    )


def _outer_frame(
    blocked_faces: set[str],
    *,
    continuous_faces: set[str],
    rail_suffix: str,
) -> list[Element]:
    """Trim rail ends and explicitly own each remaining exposed corner once."""

    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    elements: list[Element] = []
    axial_faces = {
        "x": ("west", "east"),
        "y": ("down", "up"),
        "z": ("north", "south"),
    }
    axis_index = {"x": 0, "y": 1, "z": 2}
    for name, original_from, original_to, faces in FRAME_EDGES:
        if faces & blocked_faces:
            continue
        axis = name.removeprefix("edge-")[0]
        from_ = list(original_from)
        to = list(original_to)
        low_face, high_face = axial_faces[axis]
        from_[axis_index[axis]] = 0 if low_face in continuous_faces else 1.25
        to[axis_index[axis]] = 16 if high_face in continuous_faces else 14.75
        rail = builder.box(name + rail_suffix, from_, to, "#trim")
        for face in axial_faces[axis]:
            # A normal endpoint disappears into its explicit corner cube. A
            # connected endpoint uses the recessed continuation cap below.
            # An interface-open endpoint has no corner, so its rail end must
            # retain the outward cap instead of exposing an untextured hole.
            if face not in blocked_faces or face in continuous_faces:
                rail["faces"].pop(face, None)
        elements.append(rail)
        if low_face in continuous_faces:
            elements.append(
                _rail_end_cap(
                    builder,
                    name + rail_suffix,
                    low_face,
                    from_,
                    to,
                )
            )
        if high_face in continuous_faces:
            elements.append(
                _rail_end_cap(
                    builder,
                    name + rail_suffix,
                    high_face,
                    from_,
                    to,
                )
            )

    for x_name, x1, x2 in (("west", 0, 1.25), ("east", 14.75, 16)):
        for y_name, y1, y2 in (("down", 0, 1.25), ("up", 14.75, 16)):
            for z_name, z1, z2 in (("north", 0, 1.25), ("south", 14.75, 16)):
                corner_faces = {x_name, y_name, z_name}
                if corner_faces & blocked_faces:
                    continue
                elements.append(
                    builder.box(
                        f"corner-{x_name}-{y_name}-{z_name}",
                        [x1, y1, z1],
                        [x2, y2, z2],
                        "#trim",
                    )
                )
    return elements


def _rail_end_cap(
    builder: ShapeBuilder,
    rail_name: str,
    face: str,
    rail_from: list[float],
    rail_to: list[float],
) -> Element:
    """Recess a continuation cap so adjacent blocks never share its plane."""

    axis_and_side = {
        "west": (0, False),
        "east": (0, True),
        "down": (1, False),
        "up": (1, True),
        "north": (2, False),
        "south": (2, True),
    }
    axis, high_side = axis_and_side[face]
    from_ = list(rail_from)
    to = list(rail_to)
    if high_side:
        from_[axis] = 15.98
        to[axis] = 15.99
    else:
        from_[axis] = 0.01
        to[axis] = 0.02
    cap = builder.box(
        f"{rail_name}-cap-{face}",
        from_,
        to,
        "#trim",
        append=False,
    )
    cap["faces"] = {face: cap["faces"][face]}
    return cap


def concave_elbow(elbow_name: str) -> list[Element]:
    """Return one chunky post with overlap-safe caps for a true concave elbow."""

    expected = "edge-" + elbow_name
    for name, from_, to, _ in FRAME_EDGES:
        if name != expected:
            continue
        builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
        post = builder.box(name, from_, to, "#trim")
        axis = elbow_name[0]
        axial_faces = {
            "x": ("west", "east"),
            "y": ("down", "up"),
            "z": ("north", "south"),
        }[axis]
        for face in axial_faces:
            post["faces"].pop(face, None)
        return [
            post,
            _rail_end_cap(builder, name, axial_faces[0], from_, to),
            _rail_end_cap(builder, name, axial_faces[1], from_, to),
        ]
    raise ValueError(f"unknown storage elbow: {elbow_name}")


def exterior_support_bands(
    connected_faces: set[str],
    *,
    excluded_faces: set[str] | None = None,
) -> list[Element]:
    """Add crate-like structural bands inside exposed vertical side panels."""

    elements: list[Element] = []
    excluded_faces = excluded_faces or set()
    side_layout = {
        "north": ("west", "east"),
        "south": ("west", "east"),
        "west": ("north", "south"),
        "east": ("north", "south"),
    }
    for face, (low_neighbor, high_neighbor) in side_layout.items():
        if face in connected_faces or face in excluded_faces:
            continue

        u1 = 0 if low_neighbor in connected_faces else 1.35
        u2 = 16 if high_neighbor in connected_faces else 14.65
        for label, y1, y2 in (("lower", 3.1, 4.45), ("upper", 11.3, 12.65)):
            elements.append(_support_band(face, label, u1, y1, u2, y2))
    return elements


def _support_band(
    face: str,
    label: str,
    u1: float,
    y1: float,
    u2: float,
    y2: float,
) -> Element:
    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    bounds = {
        "north": ([u1, y1, 0.35], [u2, y2, 1.25]),
        "south": ([u1, y1, 14.75], [u2, y2, 15.65]),
        "west": ([0.35, y1, u1], [1.25, y2, u2]),
        "east": ([14.75, y1, u1], [15.65, y2, u2]),
    }
    from_, to = bounds[face]
    band = builder.box(f"{face}-support-band-{label}", from_, to, "#trim", append=False)
    backing_face = {
        "north": "south",
        "south": "north",
        "west": "east",
        "east": "west",
    }[face]
    band["faces"].pop(backing_face, None)
    return band
