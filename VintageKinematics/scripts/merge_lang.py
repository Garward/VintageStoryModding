#!/usr/bin/env python3
"""Split and merge Vintage Kinematics language files."""

from __future__ import annotations

import argparse
import json
import sys
from collections import OrderedDict
from pathlib import Path


DEFAULT_LOCALE = "en"


class LangError(Exception):
    pass


def project_root_from_script() -> Path:
    return Path(__file__).resolve().parents[1]


def load_json_object(path: Path) -> OrderedDict[str, str]:
    def hook(pairs: list[tuple[str, str]]) -> OrderedDict[str, str]:
        obj: OrderedDict[str, str] = OrderedDict()
        for key, value in pairs:
            if key in obj:
                raise LangError(f"{path}: duplicate key {key!r}")
            obj[key] = value
        return obj

    try:
        with path.open("r", encoding="utf-8") as handle:
            data = json.load(handle, object_pairs_hook=hook)
    except json.JSONDecodeError as exc:
        raise LangError(f"{path}: invalid JSON: {exc}") from exc

    if not isinstance(data, dict):
        raise LangError(f"{path}: expected a JSON object")

    return data


def write_json_object(path: Path, data: OrderedDict[str, str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=4)
        handle.write("\n")


def lang_source_dir(project: Path, locale: str) -> Path:
    return project / "langsrc" / locale


def lang_output_file(project: Path, locale: str) -> Path:
    return project / "assets" / "vintagekinematics" / "lang" / f"{locale}.json"


def source_files(project: Path, locale: str) -> list[Path]:
    src_dir = lang_source_dir(project, locale)
    return sorted(path for path in src_dir.glob("*.json") if path.is_file())


def order_file(project: Path, locale: str) -> Path:
    return lang_source_dir(project, locale) / "_order.txt"


def load_key_order(project: Path, locale: str) -> list[str]:
    path = order_file(project, locale)
    if not path.exists():
        return []

    keys: list[str] = []
    seen: set[str] = set()
    with path.open("r", encoding="utf-8") as handle:
        for lineno, raw_line in enumerate(handle, start=1):
            key = raw_line.strip()
            if not key:
                continue
            if key in seen:
                raise LangError(f"{path}:{lineno}: duplicate ordered key {key!r}")
            keys.append(key)
            seen.add(key)
    return keys


def merge(project: Path, locale: str) -> None:
    files = source_files(project, locale)
    if not files:
        raise LangError(f"{lang_source_dir(project, locale)} has no language source files")

    merged: OrderedDict[str, str] = OrderedDict()
    owners: dict[str, Path] = {}
    for path in files:
        chunk = load_json_object(path)
        for key, value in chunk.items():
            if key in merged:
                raise LangError(f"{path}: key {key!r} already defined in {owners[key]}")
            merged[key] = value
            owners[key] = path

    ordered_keys = load_key_order(project, locale)
    if ordered_keys:
        ordered: OrderedDict[str, str] = OrderedDict()
        missing = [key for key in ordered_keys if key not in merged]
        if missing:
            raise LangError(f"{order_file(project, locale)} references missing key(s): {', '.join(missing[:5])}")

        for key in ordered_keys:
            ordered[key] = merged[key]
        for key, value in merged.items():
            if key not in ordered:
                ordered[key] = value
        merged = ordered

    write_json_object(lang_output_file(project, locale), merged)
    print(f"Merged {len(files)} source file(s), {len(merged)} key(s) -> {lang_output_file(project, locale)}")


def category_for_key(key: str) -> str:
    local = key.split(":", 1)[1] if ":" in key else key

    if key.startswith("game:tabname-") or "handbook-category" in key:
        return "00-categories.json"

    if local.startswith("vkguide-"):
        return "70-guides.json"

    if "block-handbooktitle-" in local or "block-handbooktext-" in local:
        return "60-handbook.json"

    if "item-handbooktitle-" in local or "item-handbooktext-" in local:
        return "60-handbook.json"

    if local.startswith("blockhelp-") or local.startswith("heldhelp-"):
        return "40-interactions.json"

    if local.startswith("block-") or local.startswith("item-"):
        return "10-blocks-items.json"

    if local.startswith("blockdesc-") or local.startswith("itemdesc-") or local.startswith("placefailure-"):
        return "20-descriptions.json"

    gui_markers = (
        "-title",
        "-input",
        "-outputs",
        "-output",
        "-fuel",
        "-die",
        "-operation",
        "-recipes",
        "-search",
        "-filter",
        "-sort",
        "-mode",
        "-apply",
        "-launch",
        "-distance",
        "-angle",
        "-rods",
        "-pipes",
        "-button",
        "basin-",
        "jsonprocessor-",
    )
    if any(marker in local for marker in gui_markers):
        return "30-gui.json"

    tooltip_markers = (
        "-status",
        "-tooltip",
        "-info",
        "-charge",
        "-depth",
        "-halted",
        "-paused",
        "-retracting",
        "-active",
        "-idle",
        "-missing",
        "-needs",
        "-upgraded",
        "-stored",
        "-contained",
        "-lastmoved",
    )
    if any(marker in local for marker in tooltip_markers):
        return "50-tooltips-status.json"

    return "90-misc.json"


def split(project: Path, locale: str, force: bool) -> None:
    source = lang_output_file(project, locale)
    data = load_json_object(source)
    target_dir = lang_source_dir(project, locale)

    if target_dir.exists() and any(target_dir.glob("*.json")) and not force:
        raise LangError(f"{target_dir} already has source files; pass --force to overwrite them")

    buckets: dict[str, OrderedDict[str, str]] = {}
    for key, value in data.items():
        category = category_for_key(key)
        buckets.setdefault(category, OrderedDict())[key] = value

    target_dir.mkdir(parents=True, exist_ok=True)
    for old_file in target_dir.glob("*.json"):
        old_file.unlink()

    for name in sorted(buckets):
        write_json_object(target_dir / name, buckets[name])

    with order_file(project, locale).open("w", encoding="utf-8") as handle:
        for key in data:
            handle.write(f"{key}\n")

    print(f"Split {len(data)} key(s) from {source} into {len(buckets)} source file(s) under {target_dir}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=project_root_from_script())
    parser.add_argument("--locale", default=DEFAULT_LOCALE)

    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("merge", help="merge langsrc/<locale>/*.json into assets/.../lang/<locale>.json")

    split_parser = subparsers.add_parser("split", help="split assets/.../lang/<locale>.json into langsrc chunks")
    split_parser.add_argument("--force", action="store_true", help="overwrite existing lang source chunks")

    args = parser.parse_args(argv)
    project = args.project.resolve()

    try:
        if args.command == "merge":
            merge(project, args.locale)
        elif args.command == "split":
            split(project, args.locale, args.force)
        else:
            raise LangError(f"unknown command {args.command}")
    except LangError as exc:
        print(f"merge_lang.py: {exc}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
