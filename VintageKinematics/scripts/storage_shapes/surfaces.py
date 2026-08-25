"""Face-local primitives used to compose storage casing surfaces."""

from __future__ import annotations

from shapegen import Element, coords

from .materials import PANEL_INSET, SKIN_DEPTH


def face_patch(
    name: str,
    face: str,
    u1: float,
    v1: float,
    u2: float,
    v2: float,
    texture: str,
    *,
    depth: float = SKIN_DEPTH,
    inset: float = 0,
) -> Element:
    """Create one outward-only rectangle in a block face's local UV plane."""

    bounds = {
        "north": ([u1, v1, inset], [u2, v2, inset + depth]),
        "south": ([u1, v1, 16 - inset - depth], [u2, v2, 16 - inset]),
        "west": ([inset, v1, u1], [inset + depth, v2, u2]),
        "east": ([16 - inset - depth, v1, u1], [16 - inset, v2, u2]),
        "down": ([u1, inset, v1], [u2, inset + depth, v2]),
        "up": ([u1, 16 - inset - depth, v1], [u2, 16 - inset, v2]),
    }
    if face not in bounds:
        raise ValueError(f"Unknown face: {face}")

    from_, to = bounds[face]
    return {
        "name": name,
        "from": coords(from_),
        "to": coords(to),
        "faces": {
            face: {
                "texture": texture,
                "uv": coords([u1, v1, u2, v2]),
            }
        },
    }


def solid_face(name: str, face: str, texture: str) -> list[Element]:
    return [face_patch(name, face, 0, 0, 16, 16, texture, inset=PANEL_INSET)]


def banded_face(name: str, face: str, texture: str = "#panel") -> list[Element]:
    """A boundary-to-boundary band with no per-block border."""

    return [
        face_patch(f"{name}-low", face, 0, 0, 16, 6.25, texture, inset=1.25),
        face_patch(f"{name}-route", face, 0, 6.25, 16, 9.75, "#route", inset=1.25),
        face_patch(f"{name}-high", face, 0, 9.75, 16, 16, texture, inset=1.25),
    ]


def grid_face(
    name: str,
    face: str,
    route_cells: set[tuple[int, int]],
    *,
    center_texture: str | None = None,
) -> list[Element]:
    """Build a three-by-three surface used by corners and junction hubs."""

    cuts = (0.0, 6.25, 9.75, 16.0)
    elements: list[Element] = []
    for u in range(3):
        for v in range(3):
            texture = "#route" if (u, v) in route_cells else "#panel"
            if center_texture is not None and (u, v) == (1, 1):
                texture = center_texture
            elements.append(
                face_patch(
                    f"{name}-{u}-{v}",
                    face,
                    cuts[u],
                    cuts[v],
                    cuts[u + 1],
                    cuts[v + 1],
                    texture,
                    inset=1.25,
                )
            )
    return elements
