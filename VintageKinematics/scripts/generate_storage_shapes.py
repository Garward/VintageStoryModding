#!/usr/bin/env python3
"""Generate exact-mask modular storage cells and oriented interface shapes."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from storage_shapes import generated_shapes
from storage_shapes.validation import validate_no_coplanar_overlap


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = ROOT / "assets/vintagekinematics/shapes/block/storage"
PROTRUDING_INTERFACE_MINIMUMS = {
    "storageport-belt-input-north.json": -4,
    "storageport-belt-output-north.json": -4,
}


def encoded(shape: dict) -> str:
    return json.dumps(shape, indent=4) + "\n"


def validate(filename: str, shape: dict) -> None:
    names: set[str] = set()
    minimum_bound = PROTRUDING_INTERFACE_MINIMUMS.get(filename, 0)
    for element in shape["elements"]:
        name = element["name"]
        if name in names:
            raise ValueError(f"{filename}: duplicate element name {name}")
        names.add(name)
        for key in ("from", "to"):
            if any(
                float(value) < minimum_bound or float(value) > 16
                for value in element[key]
            ):
                raise ValueError(f"{filename}: {name}.{key} leaves block bounds")
    validate_no_coplanar_overlap(filename, shape["elements"])


def generate(*, check: bool) -> bool:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    clean = True
    for filename, shape in generated_shapes().items():
        validate(filename, shape)
        path = OUTPUT_DIR / filename
        expected = encoded(shape)
        if check:
            matches = path.exists() and path.read_text(encoding="utf-8") == expected
            print(f"{'OK' if matches else 'STALE'} {path.relative_to(ROOT)}")
            clean &= matches
        else:
            path.write_text(expected, encoding="utf-8")
            print(f"WROTE {path.relative_to(ROOT)}")
    return clean


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="verify generated files without changing them",
    )
    args = parser.parse_args()
    return 0 if generate(check=args.check) else 1


if __name__ == "__main__":
    raise SystemExit(main())
