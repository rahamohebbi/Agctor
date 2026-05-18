# Agctor Solution Documentation

This directory contains the **solution-level** architecture documentation for the Agctor framework as a whole product.

## Documentation Files

- **system-overview-diagram**: High-level assembly and component interaction (start here)
- **architecture-diagram**: Complete system architecture across all layers
- **class-diagram**: Key classes and inheritance across all projects
- **endpoints-diagram**: All external-facing interfaces (HTTP, MCP, CLI)
- **dependencies-diagram**: Full project dependency graph with NuGet packages

## Per-Project Documentation

Each project also maintains its own `docs/` folder with detailed diagrams:

| Project | Path |
|---------|------|
| AgctorSDK.Core | `AgctorSDK.Core/docs/` |
| AgctorSDK.Agents | `AgctorSDK.Agents/docs/` |
| AgctorSDK.Tools | `AgctorSDK.Tools/docs/` |
| AgctorSDK.Extensions | `AgctorSDK.Extensions/docs/` |
| AgctorSDK.CodeGraph | `AgctorSDK.CodeGraph/docs/` |
| AgctorSDK.Host | `AgctorSDK.Host/docs/` |
| AgctorCLI | `AgctorCLI/docs/` |

## Generating Images

From a Unix shell (Linux, macOS, or **Git Bash / WSL on Windows**), after installing `@mermaid-js/mermaid-cli` globally or using `npx`:

```bash
./scripts/generate-images.sh docs
```

On **Windows**, you can run `powershell -File scripts/generate-images.ps1 -DocsDir docs` (or another `*/docs` path) to render `.mmd` files to `.jpg` using `npx` plus built-in `System.Drawing` conversion.

## Generating All Project Images

```bash
for dir in docs AgctorSDK.Core/docs AgctorSDK.Agents/docs AgctorSDK.Tools/docs AgctorSDK.Extensions/docs AgctorSDK.CodeGraph/docs AgctorSDK.Host/docs AgctorCLI/docs; do
  ./scripts/generate-images.sh "$dir"
done
```
