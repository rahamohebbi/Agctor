# Architecture Diagram

## Component overview (high level)

![Component architecture](./component-architecture-diagram.jpg)

[Edit overview source](./component-architecture-diagram.mmd)

## Detailed architecture

![Architecture Diagram](./architecture-diagram.jpg)

[Edit source](./architecture-diagram.mmd)

## Overview

AgctorSDK.Host is the HTTP and MCP gateway for the AGCTOR runtime.

## Key Components

### HTTP API Controllers
- **AgentsController** (`/api/agents`): Agent CRUD, messaging, streaming, health, type enablement
- **AgentsDefinitionsController** (`/api/agents/definitions`): Unified catalog, project-memory YAML CRUD, and **`GET …/tool-usage`** for dashboard agent→tool insight
- **GoalsController**, **ToolsController** (`/api/tools` including **`GET …/agent-associations`**), **RuntimeController**, **ConfigController**, **LlmController**
- **ScenariosController** (`/api/scenarios`), **TestController** (`/api/test`)
- **CodeGraphController**, **VisualizationController**
- **ChatProjectsController**, **ChatSessionsController**
- **ProjectMemoryController** (`/api/project-memory`), **ResolutionReviewController** (`/api/project-memory/resolution`)

### MCP Protocol
- **McpListener**: TCP server on `Mcp:Port` (default **8080**, configurable including `0` for ephemeral) routing JSON messages through **MessageDispatcher**

### Services
- **MessageDispatcher**: Routes envelopes to agents via `IActorRuntimeAdapter`
- **SqliteSessionStore** / **SqliteTraceTimelineStore**: Durable chat and trace timelines
- **AgctorToolCatalog**: Single registry of `IToolActor` types, HTTP tool ids, and **`ToolInfo`** discovery (name, description, parameters) used by **ToolInvoker** and insight APIs
- **ToolInvoker**: Direct tool execution path used by HTTP/MCP (resolves tools via the catalog)
- **ToolAgentsInsightService** (`IToolAgentsInsightService`): Merged **tool↔agent** / **agent↔tool** views from **AgctorToolCatalog** (Extensions), `IAgentFactory` registration, project-memory YAML `tools.allow`, and C# affinity hints
- **Scenario** types: catalog, factory, application service, and current-scenario store for demos and dashboard

### Background services

`TaskScoperHostedService` and `TaskFlowHostedService` are **defined in `AgctorSDK.Extensions`** (`IHostedService` implementations). The Host registers them as **singletons** and starts them from **`Program.cs` `ApplicationStarted`** (rather than `AddHostedService`) so HTTP and Swagger come up before the loops bind ports.

- **TaskScoperHostedService**: Converts goals to task DAGs (default scan interval **30s**, `TaskScoper:ScanInterval`)
- **TaskFlowHostedService**: Executes ready tasks (default interval **10s**, `TaskFlow:Interval`)

### Scenarios
- **CodeGenerationChainScenario**: LLM + CodeExecutor demo
- **CodeGraphDemoScenario**: CodeGraph pipeline with indexing, embedding coordination, search, and refactoring
