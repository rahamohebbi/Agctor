# AgctorSDK.Agents Documentation

This directory contains documentation for the AgctorSDK.Agents project.

## Documentation Files

- **architecture-diagram**: High-level system architecture
- **class-diagram**: Detailed UML class structure and relationships
- **endpoints-diagram**: Message-based API endpoints and interactions
- **dependencies-diagram**: Project dependencies and external services

Each diagram consists of three files:
- `.mmd` - Mermaid diagram source code
- `.jpg` - Generated JPEG image (high resolution, 300+ DPI)
- `.md` - Markdown documentation with image reference

## Generating Images

JPEG images are automatically generated from Mermaid source files. To generate images manually:

1. Install Mermaid CLI:
   ```bash
   npm install -g @mermaid-js/mermaid-cli
   ```

2. Run the shared generation script from the project root:
   ```bash
   ../../scripts/generate-images.sh AgctorSDK.Agents/docs
   ```

Or generate individual images:
```bash
mmdc -i architecture-diagram.mmd -o architecture-diagram.jpg -b transparent -w 2400 -H 1800 -s 2
```

## Image Requirements

- **Format**: JPEG (.jpg)
- **Resolution**: Minimum 300 DPI equivalent (2400x1800 pixels recommended)
- **Scale**: 2x for better text clarity when zoomed
- **Background**: Transparent (converted to white in JPEG)

## Updating Diagrams

1. Edit the `.mmd` file (Mermaid syntax)
2. Regenerate the `.jpg` image using the script above
3. Update the `.md` file if documentation changes are needed

## Automated Generation

For automated image generation, set up:
- **File watchers**: Regenerate images when `.mmd` files change
- **Pre-commit hooks**: Ensure images are up-to-date before commits
- **CI/CD**: Generate images as part of build process
