"""Offline pose measurements matching the modeler's current XYZ transform chain."""

from __future__ import annotations

import math

import numpy as np


def translation(value: list[float] | np.ndarray) -> np.ndarray:
    result = np.eye(4)
    result[:3, 3] = value
    return result


def rotation(degrees: list[float] | np.ndarray) -> np.ndarray:
    result = np.eye(4)
    for axis, angle in enumerate(degrees):
        c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
        matrix = np.eye(4)
        a, b = ((1, 2), (2, 0), (0, 1))[axis]
        matrix[a, a] = matrix[b, b] = c
        matrix[a, b], matrix[b, a] = -s, s
        result = result @ matrix
    return result


def point(matrix: np.ndarray, value: list[float] | np.ndarray) -> np.ndarray:
    return (matrix @ np.append(value, 1))[:3]


class Rig:
    def __init__(self, shape: dict) -> None:
        self.elements: dict[str, dict] = {}
        self.parents: dict[str, str | None] = {}

        def visit(element: dict, parent: str | None) -> None:
            name = element["name"]
            self.elements[name] = element
            self.parents[name] = parent
            for child in element.get("children", []):
                visit(child, name)

        for element in shape["elements"]:
            visit(element, None)

    def matrices(self, pose: dict, names: list[str] | None = None) -> dict[str, np.ndarray]:
        result: dict[str, np.ndarray] = {}

        def evaluate(name: str) -> np.ndarray:
            if name in result:
                return result[name]
            element = self.elements[name]
            transform = pose.get(name, {})
            origin = np.array(element.get("from", [0, 0, 0]), dtype=float)
            size = np.array(element.get("to", origin)) - origin
            pivot = np.array(element.get("rotationOrigin", origin + size / 2))
            angles = [element.get("rotation" + axis, 0) + transform.get("rotation" + axis, 0) for axis in "XYZ"]
            offsets = np.array([transform.get("offset" + axis, 0) for axis in "XYZ"])
            parent = self.parents[name]
            matrix = np.eye(4) if parent is None else evaluate(parent)
            result[name] = matrix @ translation(pivot) @ rotation(angles) @ translation(-pivot) @ translation(origin + offsets)
            return result[name]

        for name in names or list(self.elements):
            evaluate(name)
        return result


def hand_mount(item: dict, attachment: dict) -> np.ndarray:
    """EntityShapeRenderer's third-person item transform, expressed in model pixels."""
    transform = item["tpHandTransform"]
    origin = np.array([transform.get("origin", {}).get(axis, 0.5) * 16 for axis in "xyz"])
    offset = np.array([float(attachment.get("pos" + axis.upper(), 0)) + transform["translation"][axis] * 16 for axis in "xyz"])
    angles = [float(attachment.get("rotation" + axis.upper(), 0)) + transform["rotation"][axis] for axis in "xyz"]
    scale = np.eye(4)
    scale[:3, :3] *= transform.get("scale", 1)
    return translation(origin) @ scale @ translation(offset) @ rotation(angles) @ translation(-origin)
