# PRD-010 — CodeGraph demo workspace and compile gate (companion spec)

**Status:** Implemented — ships with the same release arc as the Agents dashboard (`code-graph-demo` is the default dashboard scenario).

## Purpose

When the dashboard **Apply scenario** runs `code-graph-demo`, the Host creates a temporary workspace and agents (Coder, Refactor, etc.) compile and test against it. This document records the **on-disk layout** and **compile strategy** so behavior stays explainable and regression-safe.

## Demo workspace layout (temp directory)

| Artifact | Role |
| --- | --- |
| `Demo.csproj` | SDK-style library; **`DefaultItemExcludes`** for `Tests/**` so test sources are not compiled into the demo assembly. |
| `Demo.sln` | Solution listing Demo + test project; used by `dotnet build` discovery. |
| `Calculator.cs`, `MathUtils.cs`, `ScientificCalculator.cs`, `project.md` | Root-level sources indexed by CodeGraph and edited by Coder/Refactor. |
| `Tests/AgctorSDK.Core.Tests.csproj` | xUnit project with `ProjectReference` to `..\Demo.csproj`. |
| `Tests/MathUtilsTests.cs` | Sample tests for `TestRunnerTool`. |

**Working directory:** scenario sets `Directory.SetCurrentDirectory(tempDir)` so relative paths (`Tests/...`, `MathUtils.cs`) resolve.

## Compile gate (`CompileTool` → C# file on disk)

1. If **`dotnet`** is on PATH and a **`.sln` or `.csproj`** is found walking up from the edited file, run **`dotnet build`** on that entry (restore + full graph). Failures surface MSBuild output; there is no silent fallback to Roslyn in that case.
2. Otherwise **`CSharpCompiler.CompileSameDirectoryWorkspaceAsync`**: Roslyn compiles all `*.cs` in the **same folder only** (no NuGet). Used when there is no SDK layout or no CLI.

**Implementation:** `AgctorSDK.Tools/Tools/Build/DotNetWorkspaceBuild.cs`, `CompileTool.CompileFileAsync`, `CSharpCompiler`.

## Coder pipeline

- After a successful compile: **`TestRunnerTool RunTests --path "Tests/AgctorSDK.Core.Tests.csproj"`** (path built with `Path.Combine` for cross-platform).
- Ensures packages (xUnit, etc.) come from the test project, not from Roslyn heuristics.

## Demo source robustness

- **`ScientificCalculator.Power`:** parameters are **`(double x, double y)`** matching `Math.Pow`, not `(double @base, ...)`. LLM/JSON refactors often drop `@`, which corrupts `base` (keyword) and yields errors such as **CS0161**.

## Tests (reference)

- `AgctorSDK.Core.Tests/Tools/DotNetWorkspaceBuildTests.cs` — temp solution + test project build.
- `AgctorSDK.Core.Tests/Tools/CSharpCompilerWorkspaceTests.cs` — Roslyn multi-file + fallback behavior.

## Related code

- `AgctorSDK.Host/Services/Scenarios/CodeGraphDemoScenario.cs`
- `AgctorSDK.Agents/Agents/CoderAgent.cs`
- `AgctorSDK.Tools/Tools/Implementations/CompileTool.cs`
