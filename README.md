# Agctor System

A .NET-based actor model framework for building agentic systems with pluggable backends and extensible agent capabilities.

## Overview

The Agctor System provides a robust foundation for developing agentic applications using the actor model pattern. It features:

- **Actor Model Implementation**: Core actor framework with lifecycle management and message processing
- **Pluggable Runtime Architecture**: Hot-swappable backend adapters for different deployment scenarios
- **Agent System**: Extensible agent capabilities with support for LLM integration, tool usage, and human interaction
- **Message Communication Protocol (MCP)**: Standardized message protocol for agent communication
- **HTTP + MCP Integration**: RESTful API with MCP compliance for web and TCP clients
- **Timeout Management**: Comprehensive timeout handling with progress tracking and partial result collection

## Key Features

### 🤖 Agent Types
- **LLM Agents**: Integration with local LLM services (Ollama)
- **Tool Agents**: Multi-language code execution (C#, Python)
- **Human Agents**: CLI-based human interaction for complex tasks

### 🔧 Core Components
- **Actor Runtime**: In-memory runtime with pluggable architecture
- **Agent Factory**: Multi-type agent creation and management
- **Message Dispatcher**: HTTP-to-MCP message routing
- **Timeout Supervisor**: Non-polling timeout management with adaptive policies

### 🌐 API & Integration
- **RESTful API**: Agent operations, tool invocation, and scenario management
- **MCP Server**: TCP listener for protocol compliance
- **OpenAPI/Swagger**: Auto-generated documentation and testing interface
- **CLI Interface**: Command-line utility for prompt submission

## Project Structure

```
AgctorSDK.Core/                    # Core framework and actor model
AgctorSDK.Core.Tests/              # Unit tests
AgctorSDK.Core.IntegrationTests/   # Integration tests
AgctorSDK.Host/                    # HTTP + MCP server application
AgctorCLI/                         # Command-line interface
Demo/                              # Example applications
```

## Quick Start

The Host application and its integration tests target **.NET 10** (`net10.0`). The SDK libraries, CLI, Orleans silo, demos, and remaining tests target both **.NET 8** (`net8.0`) and **.NET 10** (`net10.0`). Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build the repository. Running or testing a target requires its matching runtime; the Host and host tests require the matching ASP.NET Core runtime.

`dotnet build` builds all configured targets. The Host starts directly on .NET 10 with `dotnet run --project AgctorSDK.Host`; no framework selection is needed. For the CLI, Orleans silo, and demos, use `--framework net10.0` when running or publishing (or `--framework net8.0` for .NET 8).

### 1. Build the Solution
```bash
dotnet build Agctor.sln
```

### 2. Run the Host Application
```bash
cd AgctorSDK.Host
dotnet run
```

### 3. Use the CLI
```bash
cd AgctorCLI
dotnet run --framework net10.0 -- "Your prompt here"
```

### 4. Access the API
- Dashboard: `http://localhost:5274` (redirects to `/Dashboard`)
- REST API: `http://localhost:5274/api`
- MCP Server: `tcp://localhost:8080`
- Swagger UI: `http://localhost:5274/swagger`

## Testing

Run unit tests:
```bash
dotnet test AgctorSDK.Core.Tests --framework net10.0
```

Run integration tests:
```bash
dotnet test AgctorSDK.Core.IntegrationTests --framework net10.0
```

For test projects that target both frameworks, install both runtimes and omit `--framework` to exercise both targets. Host integration tests run on .NET 10 only. Some integration tests require local services such as Ollama.

Publish the Host or Orleans silo for .NET 10:
```bash
dotnet publish AgctorSDK.Host -c Release --framework net10.0 -o artifacts/host/net10.0
dotnet publish AgctorSDK.OrleansSilo -c Release --framework net10.0 -o artifacts/orleans/net10.0
```

The Orleans Docker image defaults to .NET 10. To build it for .NET 8 instead, pass `--build-arg DOTNET_VERSION=8.0` to `docker build`.

## Architecture

The Agctor System follows the Actor Model pattern with:
- **Actors**: Independent units of computation with state and behavior
- **Messages**: Asynchronous communication between actors
- **Supervision**: Hierarchical error handling and recovery
- **Location Transparency**: Actors can be local or distributed

## Contributing

This project emphasizes:
- **Actor Model principles**: Leverage actor patterns for concurrent and distributed systems
- **Modular design**: Avoid duplication and create reusable components
- **Documentation**: Add context and explanations to code
- **Testing**: Maintain comprehensive test coverage

## License

[Add your license information here] 

## How to run from Terminal

dotnet run --project AgctorSDK.Host --launch-profile "AgctorSDK.Host InMemory"

dotnet run --project AgctorSDK.Host --launch-profile "AgctorSDK.Host Proto"