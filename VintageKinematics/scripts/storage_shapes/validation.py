"""Geometry checks for generated storage shapes."""

from __future__ import annotations

from shapegen import Element


FACE_PLANE = {
    "north": (2, 0, (0, 1)),
    "south": (2, 1, (0, 1)),
    "west": (0, 0, (1, 2)),
    "east": (0, 1, (1, 2)),
    "down": (1, 0, (0, 2)),
    "up": (1, 1, (0, 2)),
}


def validate_no_coplanar_overlap(filename: str, elements: list[Element]) -> None:
    """Reject visible faces that claim the same positive-area plane region."""

    faces: list[tuple[str, str, int, float, tuple[float, float], tuple[float, float]]] = []
    for element in elements:
        if any(key in element for key in ("rotationX", "rotationY", "rotationZ")):
            continue
        bounds = (element["from"], element["to"])
        for face in element.get("faces", {}):
            axis, bound_index, span_axes = FACE_PLANE[face]
            faces.append(
                (
                    element["name"],
                    face,
                    axis,
                    float(bounds[bound_index][axis]),
                    (float(bounds[0][span_axes[0]]), float(bounds[1][span_axes[0]])),
                    (float(bounds[0][span_axes[1]]), float(bounds[1][span_axes[1]])),
                )
            )

    for index, first in enumerate(faces):
        for second in faces[index + 1 :]:
            if first[2] != second[2] or abs(first[3] - second[3]) > 0.0001:
                continue
            if _overlaps(first[4], second[4]) and _overlaps(first[5], second[5]):
                raise ValueError(
                    f"{filename}: coplanar faces overlap: "
                    f"{first[0]}.{first[1]} and {second[0]}.{second[1]}"
                )


def _overlaps(first: tuple[float, float], second: tuple[float, float]) -> bool:
    return min(first[1], second[1]) - max(first[0], second[0]) > 0.0001
