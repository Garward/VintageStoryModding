"""Texture aliases shared by every generated storage shape."""

TEXTURES = {
    "panel": "game:block/metal/plate/iron",
    "trim": "game:block/metal/plate/steel",
    "route": "game:block/metal/plate/copper",
    "recess": "game:block/coal/charcoal",
    "controller": "game:block/metal/plate/tinbronze",
    "belt": "vintagekinematics:block/belt",
    "shaft": "vintagekinematics:block/shaft",
}

TEXTURE_SIZE = 64
SKIN_DEPTH = 0.35
PANEL_INSET = 1.25
# Face skins occupy 1.25..1.60 on each side. Starting the solid body at 1.60
# keeps the two visible surfaces from occupying the same plane and z-fighting.
CORE_INSET = 1.60

FACE_ORDER = ("north", "east", "south", "west", "up", "down")
