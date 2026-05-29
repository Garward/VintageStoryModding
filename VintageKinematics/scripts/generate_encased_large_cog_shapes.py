#!/usr/bin/env python3
"""Refresh encased large cog assemblies from the live large cog shape."""

from __future__ import annotations

import copy
import json
from pathlib import Path
from typing import Any

from shapegen import write_json


ROOT = Path(__file__).resolve().parents[1]
SHAPE_DIR = ROOT / "assets/vintagekinematics/shapes/block"
LARGE_COG = SHAPE_DIR / "largecogwheel.json"
ENCASED = [
    SHAPE_DIR / "encasedlargecogwheel.json",
    SHAPE_DIR / "encasedlargecogwheel-shaftneg.json",
    SHAPE_DIR / "encasedlargecogwheel-shaftpos.json",
]


def load(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def assembly_children(shape: dict[str, Any]) -> list[dict[str, Any]]:
    assembly = next(element for element in shape["elements"] if element.get("name") == "cogAssembly")
    return assembly.setdefault("children", [])


def large_cog_children() -> list[dict[str, Any]]:
    shape = load(LARGE_COG)
    return [copy.deepcopy(element) for element in shape["elements"] if element.get("name") != "shaft"]


def shaft_stubs(children: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        copy.deepcopy(child)
        for child in children
        if str(child.get("name", "")).startswith("shaft")
    ]


def refresh(path: Path, cog_children: list[dict[str, Any]]) -> None:
    shape = load(path)
    children = assembly_children(shape)
    stubs = shaft_stubs(children)
    children[:] = copy.deepcopy(cog_children) + stubs
    write_json(path, shape)
    print(path)


def main() -> None:
    cog_children = large_cog_children()
    for path in ENCASED:
        refresh(path, cog_children)


if __name__ == "__main__":
    main()
