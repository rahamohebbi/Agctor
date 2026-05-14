# System Endpoints Overview

![Endpoints Diagram](./endpoints-diagram.jpg)

[Edit source](./endpoints-diagram.mmd)

## Overview

External surfaces are the **AgctorSDK.Host** HTTP API (plus Swagger), the **MCP TCP listener**, and the **AgctorCLI** executable. The Mermaid source above lists route prefixes at a glance; the **authoritative** contract is **Swagger** (`/swagger`) generated from controllers.

## HTTP

- **Default base URL**: `http://localhost:5274` when `ASPNETCORE_URLS` is not set (`AgctorSDK.Host/Program.cs`).
- **Route families**: `/api/agents`, `/api/agents/definitions`, `/api/goals`, `/api/tools`, `/api/scenarios`, `/api/test`, `/api/CodeGraph`, `/api/chat/projects`, `/api/chat/sessions`, `/api/Visualization`, `/api/project-memory`, `/api/project-memory/resolution`, `/api/Config`, `/api/Llm`, `/api/runtime`, plus Razor pages and static files.
- **Per-controller detail**: see `AgctorSDK.Host/docs/endpoints-diagram.mmd` / `.md`.

## MCP

- **TCP** server (`McpListener`): host/port from `Mcp:Host` / `Mcp:Port` (default port **8080**). Routes inbound JSON through `MessageDispatcher` to agents.

## CLI

- **AgctorCLI**: `dotnet run --project AgctorCLI` — single prompt in-process; no HTTP listener.

## Background processing

- **TaskScoperHostedService** / **TaskFlowHostedService**: intervals from `TaskScoper:ScanInterval` and `TaskFlow:Interval` (defaults 30s / 10s). Started from Host `ApplicationStarted` after HTTP is listening.
