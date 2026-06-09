#!/usr/bin/env bash
# Package the built mod into a distributable zip at <project>/dist/.
# Usage: package.sh <project-root> <release-output-dir>
set -euo pipefail

PROJ="${1:?project root required}"
OUTDIR="${2:?release output dir required}"

cd "$PROJ"

VERSION=$(grep -oP '"version"\s*:\s*"\K[^"]+' modinfo.json)
NAME=$(grep -oP '"modid"\s*:\s*"\K[^"]+' modinfo.json)

DIST="$PROJ/dist"
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

DLL=$(find "$OUTDIR" -maxdepth 1 -name '*.dll' | head -1)
if [ -z "$DLL" ]; then
    echo "package.sh: no DLL found in $OUTDIR" >&2
    exit 1
fi

mkdir -p "$DIST"
cp "$DLL" "$STAGE/"
PDB="${DLL%.dll}.pdb"
[ -f "$PDB" ] && cp "$PDB" "$STAGE/"
cp modinfo.json "$STAGE/"
[ -f modicon.png ] && cp modicon.png "$STAGE/"
[ -f README.md ] && cp README.md "$STAGE/"
[ -f CREDITS.md ] && cp CREDITS.md "$STAGE/"
[ -f LICENSE ] && cp LICENSE "$STAGE/"
[ -d assets ] && cp -r assets "$STAGE/"
[ -d "$STAGE/assets" ] && find "$STAGE/assets" -name '*.backup.*' -delete

OUT="$DIST/${NAME}-${VERSION}.zip"
rm -f "$OUT"
( cd "$STAGE" && 7z a -tzip -mx=9 "$OUT" . ) > /dev/null

echo "Packaged $OUT"
