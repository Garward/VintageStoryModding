"""Controller-specific casing face and controls."""

from __future__ import annotations

from shapegen import Element

from .casing import framed_surfaces
from .surfaces import face_patch


def controller_surfaces() -> list[Element]:
    """Build a north-facing controller; block rotation supplies other facings."""

    elements = [
        element
        for element in framed_surfaces()
        if not element["name"].startswith("north-")
    ]
    elements.extend(
        [
            face_patch("controller-background", "north", 0, 0, 16, 16, "#panel", depth=0.15, inset=0.35),
            face_patch("controller-backplate", "north", 1.25, 1.25, 14.75, 14.75, "#controller", depth=0.15, inset=0.2),
            face_patch("controller-screen", "north", 3.0, 8.0, 13.0, 13.25, "#recess", depth=0.2),
            face_patch("controller-status", "north", 3.0, 5.5, 9.25, 7.0, "#route", depth=0.18),
            face_patch("controller-keypad-a", "north", 10.25, 5.5, 11.5, 7.0, "#trim", depth=0.18),
            face_patch("controller-keypad-b", "north", 11.75, 5.5, 13.0, 7.0, "#trim", depth=0.18),
            face_patch("controller-service-panel", "north", 3.0, 2.75, 13.0, 4.5, "#panel", depth=0.22),
        ]
    )
    return elements
