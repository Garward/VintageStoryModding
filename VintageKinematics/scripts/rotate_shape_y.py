#!/usr/bin/env python3
"""Rotate a Vintage Story shape JSON around Y by quarter turns."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from shapegen import Vec3, rotate_y_elements, write_json


def parse_center(values: list[str]) -> Vec3:
    if len(values) != 3:
        raise argparse.ArgumentTypeError("center needs three numbers: x y z")
    return [float(value) for value in values]


def parse_replacement(value: str) -> tuple[str, str]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("rename replacements use old=new")
    old, new = value.split("=", 1)
    if not old:
        raise argparse.ArgumentTypeError("rename replacement old token cannot be empty")
    return old, new


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Rotate shape elements around Y using Vintage Story rotateY quarter-turn semantics.",
    )
    parser.add_argument("shape", type=Path, help="Shape JSON to rotate")
    parser.add_argument("--turns", type=int, required=True, help="Quarter turns. 1 equals rotateY 90, -1 equals rotateY 270.")
    parser.add_argument("--center", nargs=3, default=[8, 8, 8], metavar=("X", "Y", "Z"), help="Rotation center, default: 8 8 8")
    parser.add_argument("--out", type=Path, help="Output path. Defaults to overwriting only with --in-place.")
    parser.add_argument("--in-place", action="store_true", help="Overwrite the input shape")
    parser.add_argument("--rotate-children", action="store_true", help="Also rotate nested child element coordinates")
    parser.add_argument(
        "--rename",
        action="append",
        default=[],
        type=parse_replacement,
        metavar="OLD=NEW",
        help="Rename element-name tokens after rotation. Can be passed more than once.",
    )
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    center = parse_center(args.center)
    output = args.shape if args.in_place else args.out

    if output is None:
        parser.error("choose --out or --in-place")
    if args.out is not None and args.in_place:
        parser.error("choose either --out or --in-place, not both")

    shape = json.loads(args.shape.read_text(encoding="utf-8"))
    shape["elements"] = rotate_y_elements(
        shape["elements"],
        args.turns,
        center=center,
        name_replacements=args.rename,
        rotate_children=args.rotate_children,
    )
    write_json(output, shape)
    print(output)


if __name__ == "__main__":
    main()
