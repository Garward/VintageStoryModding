#!/usr/bin/env python3
"""Generate the kinetic sensor JSON shape."""

from __future__ import annotations

from pathlib import Path

from shapegen import Element, ShapeBuilder, cull_hidden_faces, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
OUT_OFF = ROOT / "assets/vintagekinematics/shapes/block/kineticsensor.json"
OUT_ON = ROOT / "assets/vintagekinematics/shapes/block/kineticsensor-on.json"
SHAFT_ORIGIN = [8, 8, 8]
SHAFT_NORTH_END = 1.22
SHAFT_SOUTH_END = 14.78
SHAFT_SPLIT_GAP = 0.08
CAP_EDGE_INSET = 0.01


def validate_in_block(elements: list[Element]) -> None:
    for element in elements:
        name = element.get("name", "<unnamed>")
        for key in ("from", "to"):
            values = element.get(key)
            if values is None:
                continue
            for axis, value in zip("xyz", values):
                if not 0 <= float(value) <= 16:
                    raise ValueError(f"{name} {key}.{axis} is outside block bounds: {value}")


def build_elements(*, light_on: bool) -> list[Element]:
    builder = ShapeBuilder(texture_size=64)
    box = builder.box

    elements: list[Element] = [
        box("shaftCoreNorth", [6, 6, SHAFT_NORTH_END], [10, 10, 8 - SHAFT_SPLIT_GAP], "#wood", rotation_origin=SHAFT_ORIGIN),
        box("shaftCoreSouth", [6, 6, 8 + SHAFT_SPLIT_GAP], [10, 10, SHAFT_SOUTH_END], "#wood", rotation_origin=SHAFT_ORIGIN),
        box("shaftEndNorth", [5.5, 5.5, CAP_EDGE_INSET], [10.5, 10.5, 1.2], "#darkwood", rotation_origin=SHAFT_ORIGIN),
        box("shaftEndSouth", [5.5, 5.5, 14.8], [10.5, 10.5, 16 - CAP_EDGE_INSET], "#darkwood", rotation_origin=SHAFT_ORIGIN),
        box("sensorCollarTop", [3.8, 10.0, 4.8], [12.2, 12.0, 11.2], "#iron"),
        box("sensorCollarBottom", [3.8, 4.0, 4.8], [12.2, 6.0, 11.2], "#iron"),
        box("sensorCollarWest", [3.8, 6.0, 4.8], [5.8, 10.0, 11.2], "#iron"),
        box("sensorCollarEast", [10.2, 6.0, 4.8], [12.2, 10.0, 11.2], "#iron"),
        box("sensorCollarNorthBand", [4.4, 4.4, 4.25], [11.6, 11.6, 5.0], "#steel"),
        box("sensorCollarSouthBand", [4.4, 4.4, 11.0], [11.6, 11.6, 11.75], "#steel"),
        box("readoutBox", [3.2, 11.5, 0.48], [12.8, 15.0, 3.35], "#wood", north="#darkwood"),
        box("readoutFacePlate", [4.0, 11.9, 0.18], [12.0, 14.45, 0.5], "#steel"),
        box("indicatorLensOff", [5.55, 12.4, 0.05], [10.45, 13.85, 0.18], "#dark"),
        box("modeTrack", [4.65, 11.55, 0.05], [11.35, 12.2, 0.18], "#copper"),
        box("modeNeedle", [7.55, 11.4, 0.0], [8.45, 12.45, 0.04], "#steel"),
        box("readoutBackBracketWest", [3.85, 10.85, 3.25], [5.25, 12.2, 4.85], "#steel"),
        box("readoutBackBracketEast", [10.75, 10.85, 3.25], [12.15, 12.2, 4.85], "#steel"),
        box("frontBraceWest", [3.55, 7.5, 2.75], [5.15, 10.45, 5.45], "#steel"),
        box("frontBraceEast", [10.85, 7.5, 2.75], [12.45, 10.45, 5.45], "#steel"),
        box("lowerCableNorth", [4.8, 3.4, 3.0], [11.2, 4.45, 5.5], "#copper"),
        box("lowerCableWest", [4.1, 3.3, 4.2], [5.0, 5.4, 11.8], "#copper"),
        box("lowerCableEast", [11.0, 3.3, 4.2], [11.9, 5.4, 11.8], "#copper"),
        box("logicOutputFrameTop", [5.0, 10.5, 0.05], [11.0, 11.2, 0.45], "#steel"),
        box("logicOutputFrameBottom", [5.0, 4.55, 0.05], [11.0, 5.5, 0.45], "#steel"),
        box("logicOutputFrameWest", [4.55, 5.5, 0.05], [5.5, 10.5, 0.45], "#steel"),
        box("logicOutputFrameEast", [10.5, 5.5, 0.05], [11.45, 10.5, 0.45], "#steel"),
        box("logicOutputFace", [6.55, 6.55, 0.0], [9.45, 9.45, 0.008], "#copper"),
        box("topScrewWest", [4.0, 14.45, 0.0], [5.25, 15.45, 0.035], "#steel"),
        box("topScrewEast", [10.75, 14.45, 0.0], [12.0, 15.45, 0.035], "#steel"),
        box("bottomScrewWest", [4.0, 11.75, 0.0], [5.25, 12.5, 0.035], "#steel"),
        box("bottomScrewEast", [10.75, 11.75, 0.0], [12.0, 12.5, 0.035], "#steel"),
    ]

    if light_on:
        elements.append(box("indicatorLensOn", [5.9, 12.65, 0.0], [10.1, 13.5, 0.035], "#glow"))

    validate_in_block(elements)
    return elements


def make_shape(*, light_on: bool) -> tuple[dict, dict[str, list[str]]]:
    elements = build_elements(light_on=light_on)
    culled = cull_hidden_faces(elements)
    shape = shape_document(
        texture_width=64,
        texture_height=64,
        textures={
            "wood": "game:block/wood/planks/generic",
            "darkwood": "game:block/wood/oak-dark",
            "iron": "game:block/metal/plate/iron",
            "steel": "game:block/metal/plate/steel",
            "copper": "game:block/metal/plate/copper",
            "dark": "game:block/coal/charcoal",
            "glow": "vintagekinematics:block/kineticigniter-rod-glow",
        },
        elements=elements,
    )
    return shape, culled


def main() -> None:
    for path, light_on in ((OUT_OFF, False), (OUT_ON, True)):
        shape, culled = make_shape(light_on=light_on)
        write_json(path, shape)
        face_count = sum(len(faces) for faces in culled.values())
        print(f"{path} ({face_count} hidden faces disabled)")


if __name__ == "__main__":
    main()
