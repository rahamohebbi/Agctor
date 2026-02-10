#!/bin/bash
# Script to generate JPEG images from Mermaid diagram files
# Requires: @mermaid-js/mermaid-cli (install via: npm install -g @mermaid-js/mermaid-cli)
# Also requires: sips (macOS) or ImageMagick convert (Linux)
#
# Usage: ./generate-images.sh [docs-directory]
# If no directory is specified, uses the current directory

set -e

# Get the directory to process (default to current directory)
DOCS_DIR="${1:-$(pwd)}"

# If relative path, resolve it relative to script location
if [[ ! "$DOCS_DIR" = /* ]]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    DOCS_DIR="$(cd "$SCRIPT_DIR/../$DOCS_DIR" && pwd)"
fi

cd "$DOCS_DIR"

# Check if mmdc is available
if ! command -v mmdc &> /dev/null; then
    echo "Error: mmdc (Mermaid CLI) not found."
    echo "Install it with: npm install -g @mermaid-js/mermaid-cli"
    exit 1
fi

# Check for image conversion tool
if command -v sips &> /dev/null; then
    CONVERT_CMD="sips -s format jpeg -s formatOptions 90"
elif command -v convert &> /dev/null; then
    CONVERT_CMD="convert -quality 90"
else
    echo "Error: No image conversion tool found (sips or convert)"
    echo "On macOS, sips should be available. On Linux, install ImageMagick."
    exit 1
fi

# Generate images for all .mmd files
for mmd_file in *.mmd; do
    if [ -f "$mmd_file" ]; then
        png_file="${mmd_file%.mmd}.png"
        jpg_file="${mmd_file%.mmd}.jpg"
        
        echo "Generating $png_file from $mmd_file..."
        mmdc -i "$mmd_file" -o "$png_file" -b white -w 2400 -H 1800 -s 2 -t dark
        
        echo "Converting $png_file to $jpg_file..."
        if command -v sips &> /dev/null; then
            sips -s format jpeg -s formatOptions 90 "$png_file" --out "$jpg_file" > /dev/null 2>&1
        else
            convert "$png_file" -quality 90 "$jpg_file"
        fi
        
        echo "✓ Generated $jpg_file (2400x1800, 2x scale)"
    fi
done

echo "All images generated successfully!"
