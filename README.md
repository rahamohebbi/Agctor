# Agctor

A .NET 8 actor-model framework for building **agentic** systems: isolated actors,
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

- **Actor runtime** with a pluggable adapter (in-memory is implemented; Orleans
  and Proto.Actor adapters are placeholders)
- **Agents**: LLM (Ollama), human/CLI, and tool actors
- **Tools**: C# and Python code execution, filesystem, code editor
- **Timeouts** as a supervisor actor, with progress and partial results
- **Host**: REST API, Swagger, and a TCP MCP listener
- **CLI** for sending a prompt to a root agent
- **Observability**: logging, metrics, and visualization helpers

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Ollama](https://ollama.com) with a local model (default `mistral`)
  for LLM agents

## Quick start

```bash
dotnet restore Agctor.sln
dotnet build Agctor.sln
```

Run the host:

```bash
dotnet run --project AgctorSDK.Host
```

- REST / Swagger: `http://localhost:5000/swagger` (see `launchSettings.json` for
  the exact URL)
- MCP: `127.0.0.1:8080` (loopback by default — see [SECURITY.md](SECURITY.md))

Run a prompt through the CLI:

```bash
dotnet run --project AgctorCLI -- "Summarize the actor model in one sentence"
```

## Testing

```bash
dotnet test AgctorSDK.Core.Tests
dotnet test AgctorSDK.Core.IntegrationTests
dotnet test AgctorSDK.Host.IntegrationTests
```

Tests tagged `RequiresOllama` need a running Ollama daemon. CI runs everything
else.

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
