# Endpoints Diagram

![Endpoints Diagram](./endpoints-diagram.jpg)

[Edit source](./endpoints-diagram.mmd)

## Overview

`AgctorSDK.Core` is a **class library** — it does not expose HTTP. Integration happens through **dependency injection extension methods** and the public contracts (`IActor`, `IAgent`, `IActorRuntimeAdapter`, project memory services, goals/tasks, observability).

## Registration helpers (typical entry points)

| Extension | Purpose |
|-----------|---------|
| `AddAgctorProjectMemory` | Project loader, rebuild, pipeline, YAML, indexing |
| `AddAgctorResolution` | Entity-resolution subsystem (PRD-018) |
| `AddInMemoryGoalStore` / `AddInMemoryTaskStore` | Goal and task persistence |
| `AddAgctorMetrics` / `AddAgctorVisualization` | Metrics and visualization wiring |
| `AddSimpleCodeGeneration` | Optional codegen helpers |

For **interface-level** method lists (`IAgent`, `ITaskStore`, …), see `class-diagram.mmd` and IntelliSense on the Core assembly.
