# AgctorSDK.CodeGraph Documentation

This directory contains concise documentation for `AgctorSDK.CodeGraph`.

## Documentation Files

- **architecture-diagram**: Actor and agent flow, including embedding readiness
- **class-diagram**: Main types and their relationships
- **endpoints-diagram**: Message flows between search, query, indexing, and refactoring
- **dependencies-diagram**: Project, package, and runtime dependencies

Each diagram consists of three files:
- `.mmd` - Mermaid diagram source code
- `.jpg` - Generated JPEG image (high resolution)
- `.md` - Markdown documentation with image reference

## Generating Images

Run the shared generation script from the project root:

```bash
../../scripts/generate-images.sh AgctorSDK.CodeGraph/docs
```

## Recent Update

The CodeGraph runtime now includes `EmbeddingCoordinatorAgent`, which:
- ensures embeddings are ready before semantic search
- marks embeddings stale after code changes
- centralizes embedding lifecycle state for all agents
