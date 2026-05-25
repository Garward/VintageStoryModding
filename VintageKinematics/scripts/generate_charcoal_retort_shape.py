#!/usr/bin/env python3
"""Generate the kinetic charcoal retort JSON shape."""

from __future__ import annotations

from pathlib import Path

from shapegen import Element, ShapeBuilder, clamped_uv, rotate_y_elements, shape_document, write_json


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/vintagekinematics/shapes/block/kineticcharcoalretort.json"
SHAFT_ORIGIN = [24, 8, 24]
STANDARD_BASE_RENAMES = (
    ("Front", "West"),
    ("front", "west"),
    ("Rear", "East"),
    ("rear", "east"),
    ("Back", "East"),
    ("back", "east"),
    ("Left", "South"),
    ("left", "south"),
    ("Right", "North"),
    ("right", "north"),
    ("Xneg", "Zpos"),
    ("xneg", "zpos"),
    ("Xpos", "Zneg"),
    ("xpos", "zneg"),
)


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
        box("shaftCore", [0, 6.75, 22.75], [48, 9.25, 25.25], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("stubXneg", [0, 6.5, 22.5], [4, 9.5, 25.5], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("stubXpos", [44, 6.5, 22.5], [48, 9.5, 25.5], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("draftFanBladeVertical", [3.2, 3.2, 23.1], [4.8, 12.8, 24.9], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("draftFanBladeHorizontal", [3.2, 7.1, 19.2], [4.8, 8.9, 28.8], "#steel", rotation_origin=SHAFT_ORIGIN),
        box("draftFanGuardTop", [2.6, 13.0, 19.5], [5.4, 14.2, 28.5], "#iron"),
        box("draftFanGuardBottom", [2.6, 1.8, 19.5], [5.4, 3.0, 28.5], "#iron"),
        box("draftFanGuardFront", [2.6, 3.0, 18.3], [5.4, 13.0, 19.5], "#iron"),
        box("draftFanGuardBack", [2.6, 3.0, 28.5], [5.4, 13.0, 29.7], "#iron"),
    ]

    for band_name, x1, x2 in (("retortBandLeft", 8.0, 9.2), ("retortBandCenter", 23.4, 24.6), ("retortBandRight", 38.8, 40.0)):
        elements.extend(retort_band(builder, band_name, x1, x2))

    for index, x in enumerate((13, 19, 29, 35), start=1):
        elements.append(box(f"hatchClamp{index}", [x, 14.4, 4.9], [x + 1.4, 25.0, 5.35], "#steel"))

    return elements


def add_shaft_hubs(elements: list[Element]) -> list[Element]:
    elements.extend(
        [
            shaft_hub("hubZpos", 24.5, 27.95),
            shaft_hub("hubZneg", -11.95, -8.5),
        ]
    )
    return elements


def shaft_hub(name: str, z1: float, z2: float) -> Element:
    return {
        "name": name,
        "from": [0, 0, 0],
        "to": [0, 0, 0],
        "rotationOrigin": [8, 8, 8],
        "faces": {},
        "children": [
            final_box(f"{name}Top", [5.4, 9.55, z1], [10.6, 10.6, z2], "#iron"),
            final_box(f"{name}Bottom", [5.4, 5.4, z1], [10.6, 6.45, z2], "#iron"),
            final_box(f"{name}West", [5.4, 6.45, z1], [6.45, 9.55, z2], "#iron"),
            final_box(f"{name}East", [9.55, 6.45, z1], [10.6, 9.55, z2], "#iron"),
        ],
    }


def position_bellows_inputs(elements: list[Element]) -> list[Element]:
    elements.extend(
        [
            final_box("northBellowsSlot", [-12.05, 4.6, -11.4], [-11.35, 11.4, -4.6], "#steel", west="#dark"),
            final_box("northBellowsCollarTop", [-12.15, 10.9, -11.9], [-11.25, 11.9, -4.1], "#copper"),
            final_box("northBellowsCollarBottom", [-12.15, 4.1, -11.9], [-11.25, 5.1, -4.1], "#copper"),
            final_box("northBellowsCollarNorth", [-12.15, 4.1, -11.9], [-11.25, 11.9, -10.9], "#copper"),
            final_box("northBellowsCollarSouth", [-12.15, 4.1, -5.1], [-11.25, 11.9, -4.1], "#copper"),
            final_box("southBellowsSlot", [-12.05, 4.6, 20.6], [-11.35, 11.4, 27.4], "#steel", west="#dark"),
            final_box("southBellowsCollarTop", [-12.15, 10.9, 20.1], [-11.25, 11.9, 27.9], "#copper"),
            final_box("southBellowsCollarBottom", [-12.15, 4.1, 20.1], [-11.25, 5.1, 27.9], "#copper"),
            final_box("southBellowsCollarNorth", [-12.15, 4.1, 20.1], [-11.25, 11.9, 21.1], "#copper"),
            final_box("southBellowsCollarSouth", [-12.15, 4.1, 26.9], [-11.25, 11.9, 27.9], "#copper"),
        ]
    )
    return elements


def final_box(
    name: str,
    from_: list[float],
    to: list[float],
    texture: str,
    *,
    north: str | None = None,
    south: str | None = None,
    east: str | None = None,
    west: str | None = None,
    up: str | None = None,
    down: str | None = None,
) -> Element:
    x = abs(to[0] - from_[0])
    y = abs(to[1] - from_[1])
    z = abs(to[2] - from_[2])
    element = {
        "name": name,
        "from": from_,
        "to": to,
        "faces": {
            "north": {"texture": texture, "uv": clamped_uv([0, 0, x, y])},
            "east": {"texture": texture, "uv": clamped_uv([0, 0, z, y])},
            "south": {"texture": texture, "uv": clamped_uv([0, 0, x, y])},
            "west": {"texture": texture, "uv": clamped_uv([0, 0, z, y])},
            "up": {"texture": texture, "uv": clamped_uv([0, 0, x, z])},
            "down": {"texture": texture, "uv": clamped_uv([0, 0, x, z])},
        },
    }
    overrides = {
        "north": north,
        "south": south,
        "east": east,
        "west": west,
        "up": up,
        "down": down,
    }
    for face, face_texture in overrides.items():
        if face_texture is not None:
            element["faces"][face]["texture"] = face_texture
    return element


def add_output_charcoal_tray(elements: list[Element]) -> list[Element]:
    elements.extend(
        [
            final_box("charcoalDischargeHood", [9.8, 13.2, -10.0], [16.75, 22.8, 26.0], "#copper", west="#dark"),
            final_box("charcoalDischargeLip", [9.4, 12.2, -8.75], [16.75, 14.2, 24.75], "#steel"),
            final_box("exhaustStack", [12.2, 23.2, -8.2], [16.2, 32.2, -4.2], "#dark"),
            final_box("exhaustStackCap", [11.2, 31.9, -9.2], [17.2, 32.7, -3.2], "#steel", up="#dark"),
            final_box("stackCollarWest", [11.2, 21.8, -9.2], [12.2, 23.2, -3.2], "#steel"),
            final_box("stackCollarEast", [16.2, 21.8, -9.2], [17.2, 23.2, -3.2], "#steel"),
            final_box("stackCollarNorth", [12.2, 21.8, -9.2], [16.2, 23.2, -8.2], "#steel"),
            final_box("stackCollarSouth", [12.2, 21.8, -4.2], [16.2, 23.2, -3.2], "#steel"),
            final_box("charcoalChuteNorthWall", [15.8, 6.7, -10.8], [17.8, 15.0, -8.8], "#steel"),
            final_box("charcoalChuteSouthWall", [15.8, 6.7, 24.8], [17.8, 15.0, 26.8], "#steel"),
            final_box("charcoalChuteBackWall", [16.8, 6.7, -8.8], [18.8, 15.0, 24.8], "#steel"),
            final_box("charcoalHoodNorthLeg", [16.8, 4.1, -9.8], [18.2, 14.0, -8.4], "#steel"),
            final_box("charcoalHoodSouthLeg", [16.8, 4.1, 24.4], [18.2, 14.0, 25.8], "#steel"),
            final_box("charcoalOutfeedDeck", [15.7, 4.2, -10.8], [32.0, 5.0, 26.8], "#iron", up="#dark"),
            final_box("charcoalOutfeedRailNorth", [15.2, 5.0, -12.2], [32.0, 7.1, -10.8], "#iron"),
            final_box("charcoalOutfeedRailSouth", [15.2, 5.0, 26.8], [32.0, 7.1, 28.2], "#iron"),
            final_box("charcoalOutfeedApron", [28.2, 3.2, -9.4], [32.0, 4.15, 25.4], "#steel"),
            final_box("charcoalPileMain", [16.4, 4.55, -7.6], [25.8, 6.1, 23.4], "#dark"),
            final_box("charcoalPileNorth", [17.8, 6.1, -5.2], [23.8, 7.2, 2.8], "#dark"),
            final_box("charcoalPileSouth", [18.6, 6.1, 12.8], [26.2, 7.0, 21.8], "#dark"),
            final_box("charcoalChunkA", [16.1, 6.05, 3.8], [19.8, 7.5, 7.2], "#dark"),
            final_box("charcoalChunkB", [22.6, 6.0, 5.4], [26.4, 7.4, 10.2], "#dark"),
            final_box("charcoalChunkC", [18.2, 6.0, 22.0], [22.0, 7.2, 25.5], "#dark"),
        ]
    )
    return elements


def main() -> None:
    elements = rotate_y_elements(build_elements(), 1, name_replacements=STANDARD_BASE_RENAMES)
    elements = add_shaft_hubs(elements)
    elements = position_bellows_inputs(elements)
    elements = add_output_charcoal_tray(elements)
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
        elements=elements,
    )

    write_json(OUT, shape)
    print(OUT)


if __name__ == "__main__":
    main()
