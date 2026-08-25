"""Named storage models emitted by the generator entry point."""

from __future__ import annotations

from itertools import combinations

from shapegen import Element, shape_document

from .casing import (
    FACE_CODE,
    casing_core,
    framed_surfaces,
    masked_cell_surfaces,
)
from .controller import controller_surfaces
from .frame import (
    ELBOW_NAMES,
    concave_elbow,
    crate_frame,
    exterior_support_bands,
    isolated_outer_frame,
)
from .materials import TEXTURES, TEXTURE_SIZE
from .ports import belt_port, kinetic_port


def model(elements: list[Element], connected_faces: set[str] | None = None) -> dict:
    return shape_document(
        textures=TEXTURES,
        elements=(
            casing_core()
            + elements
            + crate_frame(connected_faces or set())
            + exterior_support_bands(connected_faces or set())
        ),
        texture_width=TEXTURE_SIZE,
        texture_height=TEXTURE_SIZE,
    )


def interface_model(
    elements: list[Element],
    *,
    open_faces: set[str] | None = None,
) -> dict:
    """Build a closed controller/port shell without intersecting corner rails."""

    open_faces = open_faces or set()
    return shape_document(
        textures=TEXTURES,
        elements=(
            casing_core()
            + elements
            + isolated_outer_frame(excluded_faces=open_faces)
            + exterior_support_bands(set(), excluded_faces={"north"})
        ),
        texture_width=TEXTURE_SIZE,
        texture_height=TEXTURE_SIZE,
    )


def generated_shapes() -> dict[str, dict]:
    """Return exact mask models plus the small set of oriented interface models."""

    shapes = {
        "storagecell-item.json": shape_document(
            textures=TEXTURES,
            elements=(
                casing_core()
                + framed_surfaces()
                + isolated_outer_frame()
                + exterior_support_bands(set())
            ),
            texture_width=TEXTURE_SIZE,
            texture_height=TEXTURE_SIZE,
        ),
        "storagecontroller-north.json": interface_model(controller_surfaces()),
        "storageport-belt-input-north.json": interface_model(
            belt_port(output=False),
            open_faces={"north"},
        ),
        "storageport-belt-output-north.json": interface_model(belt_port(output=True)),
        "storageport-kinetic-input-north.json": interface_model(kinetic_port()),
    }
    face_codes = "neswud"
    for count in range(len(face_codes) + 1):
        for selected in combinations(face_codes, count):
            mask = "".join(selected)
            connected = {FACE_CODE[code] for code in mask}
            filename = f"storagecell-mask-{mask or 'isolated'}.json"
            shapes[filename] = model(masked_cell_surfaces(mask), connected)
    for elbow_name in ELBOW_NAMES:
        shapes[f"storagecell-elbow-{elbow_name}.json"] = shape_document(
            textures=TEXTURES,
            elements=concave_elbow(elbow_name),
            texture_width=TEXTURE_SIZE,
            texture_height=TEXTURE_SIZE,
        )
    return shapes
