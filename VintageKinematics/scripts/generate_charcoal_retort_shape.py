#!/usr/bin/env python3
"""Generate the kinetic charcoal retort JSON shape."""

from __future__ import annotations

from pathlib import Path

from shapegen import Element, ShapeBuilder, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/vintagekinematics/shapes/block/kineticcharcoalretort.json"
SHAFT_ORIGIN = [24, 8, 24]


def retort_band(builder: ShapeBuilder, name: str, x1: float, x2: float) -> list[Element]:
    return [
        builder.box(f"{name}Top", [x1, 25.6, 9.0], [x2, 28.4, 23.0], "#steel", append=False),
        builder.box(f"{name}Bottom", [x1, 12.2, 9.0], [x2, 14.2, 23.0], "#steel", append=False),
        builder.box(f"{name}Front", [x1, 15.0, 5.7], [x2, 24.0, 8.7], "#steel", append=False),
        builder.box(f"{name}Back", [x1, 15.0, 23.3], [x2, 24.0, 26.3], "#steel", append=False),
    ]


def build_elements() -> list[Element]:
    builder = ShapeBuilder.controller_relative([1, 0, 1], texture_size=64)
    box = builder.box

    elements: list[Element] = [
        box("foundationSlab", [1, 0, 1], [47, 2, 47], "#steel"),
        box("frontSkid", [2, 2, 3], [46, 4, 6], "#iron"),
        box("rearSkid", [2, 2, 42], [46, 4, 45], "#iron"),
        box("crossBraceLeft", [5, 2.2, 6], [8, 4.2, 42], "#steel"),
        box("crossBraceCenter", [22.5, 2.2, 6], [25.5, 4.2, 42], "#steel"),
        box("crossBraceRight", [40, 2.2, 6], [43, 4.2, 42], "#steel"),
        box("fireboxShell", [4, 4, 5], [44, 12.8, 27], "#copper", top="#dark", bottom="#iron"),
        box("fireboxFrontDoor", [18, 5.4, 4.55], [30, 11.2, 5.05], "#dark"),
        box("fireboxDoorFrameTop", [17, 11.1, 4.45], [31, 12.2, 5.25], "#steel"),
        box("fireboxDoorFrameBottom", [17, 4.4, 4.45], [31, 5.5, 5.25], "#steel"),
        box("fireboxDoorFrameLeft", [16.8, 4.4, 4.45], [18, 12.2, 5.25], "#steel"),
        box("fireboxDoorFrameRight", [30, 4.4, 4.45], [31.2, 12.2, 5.25], "#steel"),
        box("ashTray", [20, 2.3, 4.2], [28, 4.4, 5.2], "#iron", north="#dark"),
        box("emberGrate", [6, 12.9, 8], [42, 13.35, 24], "#dark", top="#ember"),
        box("retortBody", [5, 14, 8], [43, 25, 24], "#iron"),
        box("retortTopFacet", [7, 25, 10], [41, 28, 22], "#iron"),
        box("retortBottomFacet", [7, 12.5, 10], [41, 14.1, 22], "#iron"),
        box("retortFrontFacet", [5, 16, 6], [43, 23, 8], "#iron"),
        box("retortBackFacet", [5, 16, 24], [43, 23, 26], "#iron"),
        box("leftEndCap", [3.8, 14, 8], [5.1, 25.5, 24], "#steel"),
        box("rightEndCap", [42.9, 14, 8], [44.2, 25.5, 24], "#steel"),
        box("frontLoadingHatch", [12, 15.1, 5.45], [36, 24.2, 6.1], "#dark"),
        box("hatchFrameTop", [11, 23.8, 5.3], [37, 25.0, 6.4], "#steel"),
        box("hatchFrameBottom", [11, 14.2, 5.3], [37, 15.4, 6.4], "#steel"),
        box("hatchFrameLeft", [10.8, 14.2, 5.3], [12.2, 25.0, 6.4], "#steel"),
        box("hatchFrameRight", [35.8, 14.2, 5.3], [37.2, 25.0, 6.4], "#steel"),
        box("hatchSealBarUpper", [14, 21.2, 5.15], [34, 22.2, 6.35], "#copper"),
        box("hatchSealBarLower", [14, 17.2, 5.15], [34, 18.2, 6.35], "#copper"),
        box("hatchHandleLeft", [15, 18.5, 4.65], [16.5, 20.9, 5.35], "#iron"),
        box("hatchHandleRight", [31.5, 18.5, 4.65], [33, 20.9, 5.35], "#iron"),
        box("charcoalInspectionSlotA", [20, 19.2, 5.0], [22.5, 20.1, 5.6], "#dark"),
        box("charcoalInspectionSlotB", [25.5, 19.2, 5.0], [28, 20.1, 5.6], "#dark"),
        box("rearServiceDeck", [4, 2.1, 31], [44, 4.1, 44], "#iron"),
        box("rearFlueManifold", [6, 15, 30], [42, 23, 46], "#copper", south="#dark"),
        box("leftDraftNozzle", [10, 7, 45.7], [16, 13, 47.7], "#copper", south="#dark"),
        box("rightDraftNozzle", [32, 7, 45.7], [38, 13, 47.7], "#copper", south="#dark"),
        box("exhaustStack", [38, 22, 41], [43, 31.4, 46], "#dark"),
        box("exhaustStackCap", [37, 31.2, 40], [44, 32, 47], "#steel", top="#dark"),
        box("stackCollarFront", [37, 21.2, 40.2], [44, 23, 41.2], "#steel"),
        box("stackCollarBack", [37, 21.2, 45.8], [44, 23, 46.8], "#steel"),
        box("stackCollarLeft", [37, 21.2, 41.2], [38, 23, 45.8], "#steel"),
        box("stackCollarRight", [43, 21.2, 41.2], [44, 23, 45.8], "#steel"),
        box("shaftCore", [0, 6.75, 22.75], [48, 9.25, 25.25], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("stubXneg", [-2, 6.5, 22.5], [4, 9.5, 25.5], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("stubXpos", [44, 6.5, 22.5], [50, 9.5, 25.5], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("hubXneg", [3.5, 5.4, 21.4], [7.5, 10.6, 26.6], "#iron", rotation_origin=SHAFT_ORIGIN),
        box("hubXpos", [40.5, 5.4, 21.4], [44.5, 10.6, 26.6], "#iron", rotation_origin=SHAFT_ORIGIN),
        box("draftFanBladeVertical", [3.2, 3.2, 23.1], [4.8, 12.8, 24.9], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("draftFanBladeHorizontal", [3.2, 7.1, 19.2], [4.8, 8.9, 28.8], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("draftFanGuardTop", [2.6, 13.0, 19.5], [5.4, 14.2, 28.5], "#iron"),
        box("draftFanGuardBottom", [2.6, 1.8, 19.5], [5.4, 3.0, 28.5], "#iron"),
        box("draftFanGuardFront", [2.6, 3.0, 18.3], [5.4, 13.0, 19.5], "#iron"),
        box("draftFanGuardBack", [2.6, 3.0, 28.5], [5.4, 13.0, 29.7], "#iron"),
        box("leftPipeSupport", [11, 12.5, 27.2], [13, 16.5, 29.2], "#steel"),
        box("rightPipeSupport", [35, 12.5, 27.2], [37, 16.5, 29.2], "#steel"),
    ]

    for band_name, x1, x2 in (("retortBandLeft", 8.0, 9.2), ("retortBandCenter", 23.4, 24.6), ("retortBandRight", 38.8, 40.0)):
        elements.extend(retort_band(builder, band_name, x1, x2))

    for index, x in enumerate((13, 19, 29, 35), start=1):
        elements.append(box(f"hatchClamp{index}", [x, 14.4, 4.9], [x + 1.4, 25.0, 5.35], "#steel"))

    return elements


def main() -> None:
    shape = shape_document(
        texture_width=64,
        texture_height=64,
        textures={
            "steel": "game:block/metal/plate/steel",
            "iron": "game:block/metal/plate/iron",
            "copper": "game:block/metal/plate/copper",
            "dark": "game:block/coal/charcoal",
            "ember": "game:block/coal/ember",
            "wood": "game:block/wood/planks/generic",
        },
        elements=build_elements(),
    )

    write_json(OUT, shape)
    print(OUT)


if __name__ == "__main__":
    main()
