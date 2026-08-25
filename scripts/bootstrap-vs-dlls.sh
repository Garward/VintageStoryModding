#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/bootstrap-vs-dlls.sh [--version VERSION] [--output PATH] [--server-archive PATH] [--refresh]

Downloads and extracts the official Vintage Story server archive into an ignored
local cache suitable for VSPath. Defaults to:
  tmp/vs-dlls/<version>/vintagestory

The version is intentionally pinned. Bump it when this repo is updated for a
new Vintage Story release.
EOF
}

version="1.22.7"
output=""
server_archive=""
refresh=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="${2:?--version requires a value}"
      shift 2
      ;;
    --output)
      output="${2:?--output requires a value}"
      shift 2
      ;;
    --server-archive|--server-zip)
      server_archive="${2:?$1 requires a value}"
      shift 2
      ;;
    --refresh)
      refresh=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"

if [[ -z "$output" ]]; then
  output="$repo_root/tmp/vs-dlls/$version/vintagestory"
fi

output_parent="$(dirname -- "$output")"
mkdir -p "$output_parent"
cache_dir="$(cd -- "$output_parent" && pwd)"
archive_cache="$repo_root/tmp/vs-dlls/archives"

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "Missing required command: $cmd" >&2
    exit 1
  fi
}

download_server_archive() {
  mkdir -p "$archive_cache"

  local archive_name="vs_server_linux-x64_${version}.tar.gz"
  local archive_path="$archive_cache/$archive_name"
  if [[ -f "$archive_path" ]]; then
    echo "Using cached $archive_path" >&2
    printf '%s\n' "$archive_path"
    return
  fi

  local url="https://cdn.vintagestory.at/gamefiles/stable/$archive_name"
  echo "Downloading $url" >&2
  curl -L --fail --output "$archive_path" "$url"
  printf '%s\n' "$archive_path"
}

extract_archive() {
  local archive="$1"
  local dest="$2"

  mkdir -p "$dest"
  case "$archive" in
    *.tar.gz|*.tgz)
      tar -xzf "$archive" -C "$dest"
      ;;
    *.zip)
      "$python_exe" - "$archive" "$dest" <<'PY'
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as archive:
    archive.extractall(sys.argv[2])
PY
      ;;
    *)
      echo "Unsupported server archive extension: $archive" >&2
      exit 1
      ;;
  esac
}

validate_cache() {
  local root="$1"
  local missing=0
  local required=(
    VintagestoryAPI.dll
    VintagestoryLib.dll
    Lib/0Harmony.dll
    Lib/cairo-sharp.dll
    Lib/Newtonsoft.Json.dll
    Lib/protobuf-net.dll
    Mods/VSEssentials.dll
    Mods/VSSurvivalMod.dll
  )

  for path in "${required[@]}"; do
    if [[ ! -f "$root/$path" ]]; then
      echo "Missing extracted Vintage Story DLL: $root/$path" >&2
      missing=1
    fi
  done

  return "$missing"
}

require_cmd curl
require_cmd tar

python_exe="${PythonExe:-python3}"
require_cmd "$python_exe"

if [[ "$refresh" == "1" ]]; then
  rm -rf "$output"
fi

if validate_cache "$output" >/dev/null 2>&1; then
  echo "Vintage Story DLL cache already present: $output"
  exit 0
fi

if [[ -z "$server_archive" ]]; then
  server_archive="$(download_server_archive)"
fi

if [[ ! -f "$server_archive" ]]; then
  echo "Server archive not found: $server_archive" >&2
  exit 1
fi

tmp_extract="$(mktemp -d "$cache_dir/.extract.XXXXXX")"
trap 'rm -rf "$tmp_extract"' EXIT

echo "Extracting $server_archive -> $output"
extract_archive "$server_archive" "$tmp_extract"

rm -rf "$output"
mkdir -p "$(dirname "$output")"

if [[ -f "$tmp_extract/VintagestoryAPI.dll" ]]; then
  mv "$tmp_extract" "$output"
else
  extracted_root="$(find "$tmp_extract" -maxdepth 3 -type f -name VintagestoryAPI.dll -printf '%h\n' -quit)"
  if [[ -z "$extracted_root" ]]; then
    echo "Could not find VintagestoryAPI.dll in extracted archive." >&2
    exit 1
  fi
  mv "$extracted_root" "$output"
fi

trap - EXIT
rm -rf "$tmp_extract"

validate_cache "$output"
printf '%s\n' "$version" > "$output/.vintagestory-version"
echo "Vintage Story DLL cache ready: $output"
