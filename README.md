# Agctor

A .NET 8 and .NET 10 actor-model framework for building **agentic** systems: isolated actors,
async message passing, LLM agents, tools, and an HTTP + MCP host.

[![CI](https://github.com/rahamohebbi/Agctor/actions/workflows/ci.yml/badge.svg)](https://github.com/rahamohebbi/Agctor/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

> If you use Agctor, please keep the copyright notices and [NOTICE](NOTICE)
> file, and cite the project with [CITATION.cff](CITATION.cff).

## Why actor model

Agents in Agctor are actors. Each one owns its state, receives messages one at a
time, and talks to other agents only through envelopes. That keeps LLM calls,
tool execution, and human input isolated instead of sharing a global workflow
object.

## Features

- **Actor runtime** with in-memory and Proto.Actor adapters, plus an Orleans integration that currently delegates agent execution to in-memory actors
- **Agents**: LLM (Ollama), human/CLI, and tool actors
- **Tools**: C# and Python code execution, filesystem, code editor
- **Timeouts** as a supervisor actor, with progress and partial results
- **Host**: agent dashboard, scenario workflows, REST API, Swagger, and a TCP MCP listener
- **Project memory and CodeGraph**: project knowledge, retrieval providers, and code analysis
- **CLI** for sending a prompt to a root agent
- **Observability**: logging, metrics, and visualization helpers

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- .NET 8 runtime to run tests or applications targeting `net8.0`
- Optional: [Ollama](https://ollama.com) with a local model (default `mistral`)
  for LLM agents

## Quick start

The Host and its integration tests target .NET 10. SDK libraries, CLI, Orleans silo, demos, and remaining tests target both .NET 8 and .NET 10. `dotnet build` builds all configured targets. The Host starts directly on .NET 10 without framework selection; use `--framework net10.0` (or `net8.0`) when running other applications.

```bash
dotnet restore Agctor.sln
dotnet build Agctor.sln
```

Run the host:

```bash
dotnet run --project AgctorSDK.Host
```

- Dashboard: `http://localhost:5274` (redirects to `/Dashboard`)
- REST / Swagger: `http://localhost:5274/swagger`
- MCP: `127.0.0.1:8080` (loopback by default — see [SECURITY.md](SECURITY.md))

Run a prompt through the CLI:

```bash
dotnet run --project AgctorCLI --framework net10.0 -- "Summarize the actor model in one sentence"
```

## Scene Memory MVP

Open **Scene Memory** in the Host dashboard (`/Dashboard/SceneMemory`). Create a workspace,
add entities (characters, objects, locations, audio or music), and record explicit property changes.
For example, add `CupA`, then save `location = On the kitchen table` with a note such as
`Clip 1: ActorA puts CupA down`. Record another value when its location changes.

Scene Memory also appears in **RAG providers**, with its own `SceneMemory` kind and an **Open Scene Memory**
action. Knowledge RAG providers retrieve document/graph context; Scene Memory retrieves explicitly recorded
entity state and history. It works alongside the default RAG provider and uses its own store/tool contract.
The catalog API exposes `kind` and `canBeDefaultProvider`; Scene Memory cannot replace the default document
retrieval adapter. Markdown-only retrieval is labeled File memory.

RAG and Scene Memory share `IRetrievalProvider<TRequest, TResult>` for provider identity, health and
typed queries. `IRagProviderAdapter` adds document ingestion; `ISceneMemoryProvider` returns scene state
and history. Host APIs and scene agent tools use the same injected Scene Memory provider. Register it
outside Host with `services.AddAgctorSceneMemory(databasePath)`; pre-registered custom stores and
providers are preserved. This keeps retrieval separate from persistence and domain-specific writes.

Query memory directly, or create a test agent on the same page and ask `Where is CupA?`.
The test agent uses AGCTOR's runtime and registered `SceneMemoryQueryTool`; no AI model is required.
Both paths return the stored properties with their source change numbers. An optional change number
retrieves an earlier state. Offscreen entities retain their last recorded values.

This MVP is deterministic: questions match an entity's exact name (case insensitive), or callers
can supply its ID. Ambiguous matches ask for an explicit entity. It does not interpret arbitrary
questions, infer facts from event notes, analyze media, generate clips, or integrate with Cynelo yet.
Audio/music references can be stored as text properties; media files are not uploaded or analyzed.

`ISceneMemoryStore` in Core works independently of actors and RAG providers. SQLite stores the
workspaces and append-only change history at `data/scene-memory.db` under the Host output directory.
Set `Agctor:SceneMemory:StorePath` (environment: `Agctor__SceneMemory__StorePath`) to a durable absolute
path for deployments. Test agents are temporary and must be recreated after a Host/runtime restart.
Sequence numbers represent entry order, not a parsed film timeline. Correct a fact by recording a new value.

The provider API follows the RAG pattern: `GET /api/scene-memory/health` uses the same health response
shape, and `POST /api/scene-memory/query` accepts `workspaceId`, `query` or `entityId`, and optional
`atSequence`. Query responses share `success`, `providerId` and `message` with RAG, plus a typed `result`
containing scene facts, source changes and the requested sequence.

Existing workspace endpoints remain under `/api/scene-memory/workspaces`: create/list workspaces, then use
`/{id}/entities`, `/{id}/facts`, and `/{id}/query`. Query bodies accept `entityId` or `question`, plus
optional `atSequence`. `/{id}/agents` creates/lists test agents, and `/{id}/agents/{agentId}/query`
exercises the agent-to-tool path. Other agents can invoke the registered `SceneMemoryQueryTool`
with operation `Query` and parameters `workspaceId`, `entityId` or `query` (`question` remains an alias), and optional `atSequence`.
The Host tool catalog also exposes it as `scene-memory-query`. Like the existing Host APIs, these
endpoints assume a trusted local environment; workspace separation is not an authorization boundary.

## Testing

```bash
dotnet test AgctorSDK.Core.Tests
dotnet test AgctorSDK.Core.IntegrationTests
dotnet test AgctorSDK.Host.IntegrationTests
```

Tests tagged `RequiresOllama` need a running Ollama daemon. CI excludes those core integration tests. Multi-targeted tests run on both runtimes by default; use `--framework net10.0` to test only .NET 10. Host integration tests run on .NET 10 only.

Publish the Host or Orleans silo for .NET 10:

```bash
dotnet publish AgctorSDK.Host -c Release --framework net10.0 -o artifacts/host/net10.0
dotnet publish AgctorSDK.OrleansSilo -c Release --framework net10.0 -o artifacts/orleans/net10.0
```

The Orleans Docker image defaults to .NET 10. Pass `--build-arg DOTNET_VERSION=8.0` to build it for .NET 8.

## Repository layout

```
AgctorSDK.Core/                 # Contracts, messages, timeouts, observability
AgctorSDK.Agents/               # Agent actors and in-memory runtime
AgctorSDK.Tools/                # Tool actors (code, files)
AgctorSDK.Extensions/           # DI: AddAgctor()
AgctorSDK.Host/                 # HTTP + MCP gateway
AgctorCLI/                      # Command-line runner
AgctorSDK.Core.Tests/           # Unit tests
AgctorSDK.Core.IntegrationTests/
AgctorSDK.Host.IntegrationTests/
Demo/                           # Visualization samples
Project/                        # Product requirements (not source)
```

## Using it as a library

```csharp
using AgctorSDK.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgctor(); // in-memory runtime by default
```

Packable projects (`Core`, `Agents`, `Tools`, `Extensions`) are Apache-2.0 and
include LICENSE + NOTICE in the nupkg.

## Security

Code execution and filesystem tools run **in-process with no sandbox**. The host
has no authentication yet. Use Agctor only in environments you trust. Details:
[SECURITY.md](SECURITY.md).

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and small, tested
PRs are the fastest path to a merge.

## Citation

```bash
# GitHub: use "Cite this repository" from CITATION.cff
```

## License

Copyright 2026 Raha Mohebbi and Agctor contributors.

Licensed under the [Apache License, Version 2.0](LICENSE).
You must retain attribution (copyright headers / NOTICE) when you redistribute.
See [NOTICE](NOTICE).
