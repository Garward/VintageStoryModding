"""Shared casing and topology-specific cell surface assemblies."""

from __future__ import annotations

from shapegen import Element, ShapeBuilder

from .materials import CORE_INSET, FACE_ORDER, PANEL_INSET, TEXTURE_SIZE
from .surfaces import face_patch, solid_face


FACE_CODE = {
    "n": "north",
    "e": "east",
    "s": "south",
    "w": "west",
    "u": "up",
    "d": "down",
}

FACE_PANEL_NEIGHBORS = {
    # Local u-low, v-low, u-high, v-high around each visible face.
    "north": ("west", "down", "east", "up"),
    "south": ("west", "down", "east", "up"),
    "west": ("north", "down", "south", "up"),
    "east": ("north", "down", "south", "up"),
    "down": ("west", "north", "east", "south"),
    "up": ("west", "north", "east", "south"),
}

def casing_core() -> list[Element]:
    builder = ShapeBuilder(texture_size=TEXTURE_SIZE)
    return [
        builder.box(
            "casing-core",
            [CORE_INSET, CORE_INSET, CORE_INSET],
            [16 - CORE_INSET, 16 - CORE_INSET, 16 - CORE_INSET],
            "#panel",
        )
    ]


def framed_surfaces() -> list[Element]:
    """Panels bounded by the inner edge of an overlap-free outer frame."""

    return [
        face_patch(
            f"{face}-framed-panel",
            face,
            PANEL_INSET,
            PANEL_INSET,
            16 - PANEL_INSET,
            16 - PANEL_INSET,
            "#panel",
            inset=PANEL_INSET,
        )
        for face in FACE_ORDER
    ]


def masked_cell_surfaces(mask: str) -> list[Element]:
    """Quiet exterior panels and hidden core joins for one exact face mask."""

    connected = {FACE_CODE[code] for code in mask}
    elements: list[Element] = []
    for face in FACE_ORDER:
        if face in connected:
            # Extend only the core cross-section to the block boundary. A full-face plug
            # protrudes past the recessed exterior panels and appears as a paper-thin wall
            # between cells when viewed from an adjacent exposed face.
            elements.append(
                face_patch(
                    f"{face}-joined-body",
                    face,
                    PANEL_INSET,
                    PANEL_INSET,
                    16 - PANEL_INSET,
                    16 - PANEL_INSET,
                    "#panel",
                    depth=CORE_INSET,
                    inset=0,
                )
            )
        else:
            u_low, v_low, u_high, v_high = FACE_PANEL_NEIGHBORS[face]
            elements.append(
                face_patch(
                    f"{face}-exterior-panel",
                    face,
                    0 if u_low in connected else PANEL_INSET,
                    0 if v_low in connected else PANEL_INSET,
                    16 if u_high in connected else 16 - PANEL_INSET,
                    16 if v_high in connected else 16 - PANEL_INSET,
                    "#panel",
                    inset=PANEL_INSET,
                )
            )
    return elements
