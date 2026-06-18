#!/usr/bin/env python3
"""Audit VK blocktypes that still use custom block classes instead of the openable framework."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


MACHINE_WORDS = (
    "machine",
    "sawmill",
    "mixer",
    "press",
    "extractor",
    "sieve",
    "retort",
    "basin",
    "quern",
    "crusher",
    "bore",
    "trebuchet",
)


def project_root() -> Path:
    return Path(__file__).resolve().parents[1]


def read_class_info(root: Path) -> dict[str, dict[str, object]]:
    info: dict[str, dict[str, object]] = {}

    for src_dir in (root / "src/Blocks", root / "src/BlockEntities"):
        for path in sorted(src_dir.glob("*.cs")):
            text = path.read_text(errors="replace")
            for match in re.finditer(
                r"\b(?:public\s+|private\s+|internal\s+|sealed\s+|abstract\s+|partial\s+)*"
                r"class\s+(\w+)\s*(?::\s*([^{\n]+))?",
                text,
            ):
                name = match.group(1)
                bases = (match.group(2) or "").strip()
                info[name] = {
                    "file": path.relative_to(root).as_posix(),
                    "bases": bases,
                    "inherits_openable": "BlockKineticOpenableMachine" in bases,
                    "placement": any(
                        token in text
                        for token in (
                            "TryResolvePlacementPreview",
                            "TryPlaceBlock",
                            "SideFacingPlayer",
                        )
                    ),
                    "interact": "OnBlockInteractStart" in text,
                    "io": "BuildIOFaceMap" in text or "vkIo" in text,
                    "rotation": any(
                        token in text
                        for token in (
                            "TryResolvePlacementPreview",
                            "TryPlaceBlock",
                            "SideFacingPlayer",
                            "OutputSideFacingPlayer",
                            "BlockFacing.HorizontalFromYaw",
                            "BlockFacing.FromFirstLetter",
                            "BlockFacing.FromCode",
                            "Block?.Shape?.rotateY",
                            "Shape?.rotateY",
                            'Variant["side"]',
                            'Variant["axis"]',
                            'Variant?["side"]',
                            'Variant?["axis"]',
                        )
                    ),
                    "liquid_sink": "ILiquidSink" in bases,
                }

    return info


def read_entity_registrations(root: Path) -> dict[str, str]:
    modsystem = root / "src/VintageKinematicsModSystem.cs"
    text = modsystem.read_text(errors="replace")
    registrations: dict[str, str] = {}

    for match in re.finditer(
        r'RegisterBlockEntityClass\("([^"]+)",\s*typeof\(BlockEntities\.(\w+)\)\)',
        text,
    ):
        registrations[match.group(1)] = match.group(2)

    return registrations


def read_blocktypes(root: Path, class_info: dict[str, dict[str, object]]) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    entity_registrations = read_entity_registrations(root)
    blocktype_dir = root / "assets/vintagekinematics/blocktypes"

    for path in sorted(blocktype_dir.glob("*.json")):
        data = json.loads(path.read_text())
        block_class = data.get("class", "")
        if not block_class:
            continue

        attrs = data.get("attributes") or {}
        info = class_info.get(block_class, {})
        entity = data.get("entityClass", "")
        be_class = entity_registrations.get(entity, "")
        be_info = class_info.get(be_class, {})
        rows.append(
            {
                "code": data.get("code", path.stem),
                "file": path.name,
                "class": block_class,
                "entity": entity,
                "be_class": be_class,
                "framework": block_class == "BlockKineticOpenableMachine"
                or bool(info.get("inherits_openable", False)),
                "has_vkIo": "vkIo" in attrs,
                "bases": info.get("bases", ""),
                "src": info.get("file", ""),
                "be_src": be_info.get("file", ""),
                "be_bases": be_info.get("bases", ""),
                "placement": bool(info.get("placement", False)),
                "interact": bool(info.get("interact", False)),
                "io": bool(info.get("io", False)),
                "be_io": bool(be_info.get("io", False)),
                "rotation": bool(info.get("rotation", False)),
                "be_rotation": bool(be_info.get("rotation", False)),
                "liquid_sink": bool(info.get("liquid_sink", False)),
            }
        )

    return rows


def is_machine_like(row: dict[str, object]) -> bool:
    haystack = " ".join(str(row.get(key, "")) for key in ("code", "file", "class", "entity")).lower()
    return any(word in haystack for word in MACHINE_WORDS)


def print_rows(title: str, rows: list[dict[str, object]]) -> None:
    print(title)
    if not rows:
        print("  none")
        return

    for row in rows:
        flags = [
            name
            for name in (
                "placement",
                "interact",
                "io",
                "be_io",
                "rotation",
                "be_rotation",
                "liquid_sink",
                "has_vkIo",
            )
            if row.get(name)
        ]
        print(
            f"  {row['file']:<32} class={row['class']:<30} "
            f"entity={row['entity']:<28} flags={','.join(flags) or '-'}"
        )
        if row.get("src"):
            print(f"    src={row['src']} bases={row['bases']}")
        if row.get("be_src"):
            print(f"    be ={row['be_src']} bases={row['be_bases']}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--all",
        action="store_true",
        help="also print every non-framework blocktype, including gears, belts, storage, and generators",
    )
    args = parser.parse_args()

    root = project_root()
    class_info = read_class_info(root)
    rows = read_blocktypes(root, class_info)

    framework_rows = [row for row in rows if row["framework"]]
    legacy_machine_rows = [
        row for row in rows if not row["framework"] and is_machine_like(row)
    ]

    print_rows("FRAMEWORK/OPENABLE BLOCKTYPES", framework_rows)
    print()
    print_rows(
        "MACHINE-LIKE BLOCKTYPES NOT USING BlockKineticOpenableMachine",
        legacy_machine_rows,
    )

    if args.all:
        print()
        print_rows(
            "ALL NON-FRAMEWORK BLOCKTYPES WITH CUSTOM C# CLASS",
            [row for row in rows if not row["framework"]],
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
