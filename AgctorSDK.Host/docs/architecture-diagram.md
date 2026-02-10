# Architecture Diagram

![Architecture Diagram](./architecture-diagram.jpg)

[Edit source](./architecture-diagram.mmd)

## Overview

AgctorSDK.Host is the ASP.NET Core Web API + MCP server that serves as the HTTP and MCP gateway for the AGCTOR agent framework.

## Key Components

### HTTP API Controllers
- **AgentsController** (`/api/agents`): Agent CRUD and messaging
- **GoalsController** (`/api/goals`): Goal CRUD
- **ToolsController** (`/api/tools`): Tool invocation and discovery
- **TestController** (`/api/test`): Test scenario setup

### MCP Protocol
- **McpListener**: TCP server (port 8080) accepting JSON-delimited messages

### Services
- **MessageDispatcher**: Routes messages to agents via actor runtime
- **ToolInvoker**: Direct tool execution without agent wrapper
- **ScenarioFactory**: Creates predefined test scenarios

### Background Services (provided by AgctorSDK.Extensions)

The background hosted services are defined in `AgctorSDK.Extensions/Services/` and registered via the `AddAgctorBackgroundServices()` extension method. This keeps the Host focused on HTTP/MCP gateway concerns while making the services reusable by any host application.

- **TaskScoperHostedService**: Converts goals to task DAGs (every 30s)
- **TaskFlowHostedService**: Executes task workflows (every 10s)

#### TaskScoperHostedService

`TaskScoperHostedService` is a background service (`IHostedService`) that periodically polls for new goals and converts them into task DAGs (Directed Acyclic Graphs). It delegates the actual decomposition logic to `TaskScoperAgent.ProcessGoalsAsync()`, which reads from `IGoalStore` and writes the resulting task graphs to `ITaskStore`. The default scan interval is **30 seconds**, configurable via `TaskScoperOptions.ScanInterval`. The service runs in a continuous loop with graceful cancellation support.

#### TaskFlowHostedService

`TaskFlowHostedService` is a background service (`IHostedService`) that periodically walks the task DAGs produced by the scoper and executes tasks whose dependencies have been satisfied. It delegates execution to `TaskFlowEngine.RunOnceAsync()`, which reads pending tasks from `ITaskStore` and dispatches them through `ITaskExecutor`. The default execution interval is **10 seconds**, configurable via `TaskFlowOptions.Interval`. Like the scoper, it runs in a continuous loop with graceful cancellation support.

### Scenarios
- **CodeGenerationChainScenario**: LLM + CodeExecutor demo
- **CodeGraphDemoScenario**: Full CodeGraph pipeline with indexing, search, and refactoring
