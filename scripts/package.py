#!/usr/bin/env python3
"""Package a built Vintage Story mod into a distributable zip."""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


DEFAULT_EXTRA_FILES = (
    "CREDITS.md",
    "LICENSE",
    "../LICENSE",
    "docs/api-tutorial.md",
)

PACKAGE_README = "PACKAGE_README.md"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--build-output", required=True, type=Path)
    parser.add_argument("--target-path", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--assembly", required=True)
    parser.add_argument("--package-name", default="")
    parser.add_argument("--extra-files", default="")
    return parser.parse_args()


def load_modinfo(project: Path) -> dict[str, object]:
    modinfo = project / "modinfo.json"
    if not modinfo.exists():
        return {}
    with modinfo.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def default_package_name(modinfo: dict[str, object], assembly: str) -> str:
    modid = str(modinfo.get("modid") or assembly).strip()
    version = str(modinfo.get("version") or "").strip()
    if version:
        return f"{modid}-{version}.zip"
    return f"{modid}.zip"


def find_target_dll(target_path: Path, build_output: Path, assembly: str) -> Path:
    if target_path.exists():
        return target_path

    named = build_output / f"{assembly}.dll"
    if named.exists():
        return named

    dlls = sorted(build_output.glob("*.dll"))
    if dlls:
        return dlls[0]

    raise FileNotFoundError(f"No DLL found in {build_output}")


def should_skip(path: Path) -> bool:
    return fnmatch.fnmatch(path.name, "*.backup.*")


def add_file(zip_file: ZipFile, source: Path, archive_name: str, seen: set[str]) -> None:
    if not source.exists() or source.is_dir() or should_skip(source):
        return

    archive_name = archive_name.replace(os.sep, "/")
    if archive_name in seen:
        return

    zip_file.write(source, archive_name)
    seen.add(archive_name)


def add_tree(zip_file: ZipFile, root: Path, archive_root: str, seen: set[str]) -> None:
    if not root.exists():
        return

    for source in sorted(path for path in root.rglob("*") if path.is_file()):
        if should_skip(source):
            continue
        relative = source.relative_to(root).as_posix()
        add_file(zip_file, source, f"{archive_root}/{relative}", seen)


def iter_extra_files(value: str) -> tuple[str, ...]:
    extras = [part.strip() for part in value.split(";") if part.strip()]
    return (*DEFAULT_EXTRA_FILES, *extras)


def add_extra(zip_file: ZipFile, project: Path, relative_path: str, seen: set[str]) -> None:
    source = (project / relative_path).resolve()
    if not source.exists():
        return

    if source.is_dir():
        add_tree(zip_file, source, Path(relative_path).name, seen)
        return

    archive_name = Path(relative_path).name if relative_path.startswith("../") else relative_path
    add_file(zip_file, source, archive_name, seen)


def add_readme(zip_file: ZipFile, project: Path, seen: set[str]) -> None:
    """Package the player README when supplied, otherwise use the project README."""
    player_readme = project / PACKAGE_README
    source = player_readme if player_readme.exists() else project / "README.md"
    add_file(zip_file, source, "README.md", seen)


def main() -> int:
    args = parse_args()
    project = args.project.resolve()
    build_output = args.build_output.resolve()
    target_dll = find_target_dll(args.target_path.resolve(), build_output, args.assembly)
    modinfo = load_modinfo(project)

    package_name = args.package_name.strip() or default_package_name(modinfo, args.assembly)
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    package_path = output_dir / package_name
    package_path.unlink(missing_ok=True)

    seen: set[str] = set()
    with ZipFile(package_path, "w", ZIP_DEFLATED) as zip_file:
        add_file(zip_file, target_dll, f"{args.assembly}.dll", seen)

        for suffix in (".deps.json",):
            add_file(zip_file, target_dll.with_suffix(suffix), f"{args.assembly}{suffix}", seen)

        add_file(zip_file, project / "modinfo.json", "modinfo.json", seen)
        add_file(zip_file, project / "modicon.png", "modicon.png", seen)
        add_readme(zip_file, project, seen)

        for relative_path in iter_extra_files(args.extra_files):
            add_extra(zip_file, project, relative_path, seen)

        add_tree(zip_file, project / "assets", "assets", seen)

    print(f"Packaged: {package_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
