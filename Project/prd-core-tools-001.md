# 🛠️ PRD: Tool Usage in Agctor

## Goal
Enable Agctor Agents to **use external Tools** in a flexible and composable way. Each Tool is modeled as an **Actor**, allowing agents to call tools just like they would call other agents.

## Why We’re Doing This
Real-world agent workflows require actions like running code, testing, and working with files — all without user interfaces. Modeling tools as actors lets us plug them into the actor system and treat them the same as any other agent.

## Core Concepts

### 1. Tool is an Actor
Each tool (like "Run Code", "Format Code", "Search Files") is a special kind of actor that handles a `ToolRequest` and returns a `ToolResult`.

### 2. Agents Use Tools via Messaging
Agents send standard `MessageEnvelope` messages to tool actors. The tool processes the input and returns the result to the sender.

### 3. Chaining and Composition
Agents can:
- Chain tools together (`Tool1 → Tool2 → Tool3`)
- Send results to other agents
- Fan-out to parallel tools and aggregate results

---

## Important Coding Tools to Implement First

| Tool Name            | Description                                                                 |
|----------------------|-----------------------------------------------------------------------------|
| `CodeExecutorTool`   | Run code snippets (C#, Python, etc.) and return output/errors              |
| `UnitTestRunnerTool` | Execute unit tests and report results                                      |
| `CodeFormatterTool`  | Format source files using appropriate linters/formatters                  |
| `CodeLinterTool`     | Perform static analysis to find errors or warnings                         |
| `GitTool`            | Perform Git actions like `clone`, `diff`, `commit`, `log`                 |
| `PromptStoreTool`    | Store prompt + result history in Git-backed folder                        |
| `DiffTool`           | Compare two versions of a file or function and return a semantic diff      |
| `FileSystemTool`     | Read from or write to disk, create folders, delete files                   |
| `ErrorExplainerTool` | Convert error output into natural-language explanations                   |

Each tool implements `IToolActor`.

---

## What You Need to Build

### ✅ ToolActor Interface
```csharp
public interface IToolActor : IActor {
    Task<ToolResult> Handle(ToolRequest request);
}