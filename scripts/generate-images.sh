#!/bin/bash
# Turn Mermaid sources (*.mmd) into high-resolution JPEG images.
# No manual export step: this script renders the diagram and writes the .jpg.
#
# Usage:
#   ./scripts/generate-images.sh [docs-directory] [file.mmd ...]
#
# Examples:
#   ./scripts/generate-images.sh docs
#   ./scripts/generate-images.sh docs architecture-diagram.mmd
#
# Needs Node (npx) for Mermaid CLI when `mmdc` is not installed.
# Converts PNG to JPEG with sips, ImageMagick, or ffmpeg.
# Windows: scripts/generate-images.ps1

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCS_DIR="${1:-$(pwd)}"
if [[ "${1:-}" != "" ]]; then
    shift
fi

# Relative docs paths are from the repo root (parent of scripts/).
if [[ "$DOCS_DIR" != /* ]]; then
    DOCS_DIR="$(cd "$SCRIPT_DIR/../$DOCS_DIR" && pwd)"
fi

cd "$DOCS_DIR"

if command -v mmdc >/dev/null 2>&1; then
    MMDC=(mmdc)
else
    MMDC=(npx --yes @mermaid-js/mermaid-cli)
fi

# Scale multiplies the rendered pixels so labels stay sharp when zoomed.
# Leave MMDC_SIZE empty. Mermaid applies --size before scale, which overgrows the JPEG.
SCALE="${MMDC_SCALE:-4}"
SIZE="${MMDC_SIZE:-}"
THEME="${MMDC_THEME:-}"

PUPPETEER_CFG=""
cleanup() {
    if [[ -n "$PUPPETEER_CFG" && -f "$PUPPETEER_CFG" ]]; then
        rm -f "$PUPPETEER_CFG"
    fi
}
trap cleanup EXIT

# Use a local Chrome when Puppeteer has not downloaded its own browser.
CHROME="${PUPPETEER_EXECUTABLE_PATH:-}"
if [[ -z "$CHROME" ]]; then
    for candidate in \
        /usr/bin/google-chrome-stable \
        /usr/bin/google-chrome \
        /usr/bin/chromium \
        /usr/bin/chromium-browser
    do
        if [[ -x "$candidate" ]]; then
            CHROME="$candidate"
            break
        fi
    done
fi

PUPPETEER_ARGS=()
if [[ -n "$CHROME" ]]; then
    PUPPETEER_CFG="$(mktemp)"
    # Chrome in CI and containers needs these flags to start.
    cat >"$PUPPETEER_CFG" <<EOF
{
  "executablePath": "$CHROME",
  "args": ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
}
EOF
    PUPPETEER_ARGS=(--puppeteerConfigFile "$PUPPETEER_CFG")
fi

png_to_jpg() {
    local png="$1"
    local jpg="$2"
    if command -v sips >/dev/null 2>&1; then
        sips -s format jpeg -s formatOptions 92 "$png" --out "$jpg" >/dev/null
    elif command -v magick >/dev/null 2>&1; then
        magick "$png" -density 300 -units PixelsPerInch -quality 92 "$jpg"
    elif command -v convert >/dev/null 2>&1; then
        convert "$png" -density 300 -units PixelsPerInch -quality 92 "$jpg"
    elif command -v ffmpeg >/dev/null 2>&1; then
        ffmpeg -y -loglevel error -i "$png" -q:v 2 "$jpg"
    else
        echo "Error: need sips, ImageMagick, or ffmpeg to write JPEG files." >&2
        exit 1
    fi
}

shopt -s nullglob
if [[ $# -gt 0 ]]; then
    mmd_files=("$@")
else
    mmd_files=(*.mmd)
fi

if [[ ${#mmd_files[@]} -eq 0 ]]; then
    echo "No .mmd files in $DOCS_DIR"
    exit 0
fi

for mmd_file in "${mmd_files[@]}"; do
    if [[ ! -f "$mmd_file" ]]; then
        echo "Missing Mermaid file: $mmd_file" >&2
        exit 1
    fi

    base="${mmd_file%.mmd}"
    png_file="${base}.png"
    jpg_file="${base}.jpg"

    theme_args=()
    if [[ -n "$THEME" ]]; then
        theme_args=(-t "$THEME")
    fi

    size_args=()
    if [[ -n "$SIZE" ]]; then
        size_args=(--size "$SIZE")
    fi

    echo "Rendering $mmd_file"
    "${MMDC[@]}" \
        -i "$mmd_file" \
        -o "$png_file" \
        -b white \
        -s "$SCALE" \
        "${size_args[@]}" \
        "${theme_args[@]}" \
        "${PUPPETEER_ARGS[@]}"

    echo "Writing $jpg_file"
    png_to_jpg "$png_file" "$jpg_file"
    rm -f "$png_file"
    echo "Generated $jpg_file"
done
