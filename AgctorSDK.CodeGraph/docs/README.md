# AgctorSDK.CodeGraph Documentation

This directory contains documentation for the AgctorSDK.CodeGraph project.

## Documentation Files

- **architecture-diagram**: High-level system architecture showing actors, agents, analyzers, embeddings, and their relationships
- **class-diagram**: Detailed UML class structure with inheritance and interface implementations
- **endpoints-diagram**: Message-based API endpoints and data flows between agents and services
- **dependencies-diagram**: Project dependencies, NuGet packages, and external services

Each diagram consists of three files:
- `.mmd` - Mermaid diagram source code
- `.jpg` - Generated JPEG image (high resolution)
- `.md` - Markdown documentation with image reference

## Generating Images

Run the shared generation script from the project root:

```bash
../../scripts/generate-images.sh AgctorSDK.CodeGraph/docs
```

## Updating Diagrams

1. Edit the `.mmd` file (Mermaid syntax)
2. Regenerate the `.jpg` image using the script above
3. Update the `.md` file if documentation changes are needed
